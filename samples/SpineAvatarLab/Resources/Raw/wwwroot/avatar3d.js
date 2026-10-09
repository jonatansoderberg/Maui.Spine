// Draws a GLB avatar for AvatarView's 3D surface (issue #524, decision D1a). The C# scheduler owns
// time: every frame arrives as a message with pose, clip and level weights, and this page only
// applies them and renders. Nothing is fetched; the model arrives in the load message.
import * as THREE from 'three';
import { GLTFLoader } from './three/GLTFLoader.js';
import { AdditiveAnimationBlendMode, AnimationUtils } from 'three';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';

let renderer, scene, camera, root, mixer;
let bindings, themes;
let nodes = [];            // glTF node index → Object3D
let baseline = [];         // node index → { position, quaternion, scale }
let morphMeshes = [];      // every mesh with morph targets
let meshesByPrimitive = new Map(); // `${node}:${primitive}` → Mesh
let materialsByIndex = new Map();
let actions = new Map();
let before = new Map();    // mesh → influences at the start of a layer
let lastStats = 0, frames = 0, renderMs = 0;
// "studio": environment light, key and rim lights, tone mapping, bloom on emissive parts, a contact
// shadow. "basic": the round-1 setup, kept for comparison.
let look = 'studio', composer, bloomComposer, bloom, environment, basicLights = [], studioLights = [], shadow, bounds;
const emissiveBase = new Map();

const send = message => window.HybridWebView.SendRawMessage(message);
const fail = error => {
    document.getElementById('error').textContent = String(error?.stack ?? error);
    send('error:' + (error?.message ?? error));
};

window.addEventListener('HybridWebViewMessageReceived', event => {
    try {
        const message = JSON.parse(event.detail.message);
        if (message.t === 'f') frame(message);
        else if (message.t === 'load') load(message).catch(fail);
        else if (message.t === 'look') setLook(message.v);
    } catch (error) {
        fail(error);
    }
});

async function load(message) {
    dispose();
    bindings = message.bindings;
    themes = message.themes;

    const bytes = Uint8Array.from(atob(message.glb), c => c.charCodeAt(0));
    const gltf = await new Promise((resolve, reject) => new GLTFLoader().parse(bytes.buffer, '', resolve, reject));
    root = gltf.scene;

    for (const [object, association] of gltf.parser.associations) {
        if (association?.nodes !== undefined && object.isObject3D) nodes[association.nodes] = object;
        if (association?.materials !== undefined && object.isMaterial) materialsByIndex.set(association.materials, object);
    }
    // A node with one primitive is the Mesh itself; with several, a Group of Meshes.
    nodes.forEach((object, index) => {
        if (!object) return;
        baseline[index] = { position: object.position.clone(), quaternion: object.quaternion.clone(), scale: object.scale.clone() };
        const meshes = object.isMesh ? [object] : object.children.filter(c => c.isMesh);
        meshes.forEach((mesh, primitive) => meshesByPrimitive.set(`${index}:${primitive}`, mesh));
    });
    root.traverse(o => { if (o.isMesh && o.morphTargetInfluences) morphMeshes.push(o); });

    renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.setClearColor(0x000000, 0);
    document.body.appendChild(renderer.domElement);

    const framing = bindings.framing ?? {};
    camera = new THREE.PerspectiveCamera(framing.verticalFov ?? 35, 1, 0.01, 100);
    camera.position.fromArray(framing.cameraPosition ?? [0, 0, 3.5]);
    camera.lookAt(...(framing.lookAt ?? [0, 0, 0]));

    scene = new THREE.Scene();
    scene.add(root);
    const hemisphere = new THREE.HemisphereLight(0xf2ffff, 0x526475, 2);
    const light = new THREE.DirectionalLight(0xffffff, 3);
    light.position.set(-2, 3, 4);
    basicLights = [hemisphere, light];

    const key = new THREE.DirectionalLight(0xfff4e8, 1.6);
    key.position.set(-2.5, 3, 4);
    const rim = new THREE.DirectionalLight(0x9fe8ff, 2.4);
    rim.position.set(2.5, 2.5, -3.5);
    const fill = new THREE.HemisphereLight(0xffffff, 0x2a3440, 0.25);
    studioLights = [key, rim, fill];
    scene.add(...basicLights, ...studioLights);

    environment = new THREE.PMREMGenerator(renderer).fromScene(new RoomEnvironment(), 0.04).texture;
    root.traverse(o => {
        for (const m of o.isMesh ? (Array.isArray(o.material) ? o.material : [o.material]) : []) {
            if (m.emissive && m.emissive.getHex() !== 0) emissiveBase.set(m, m.emissiveIntensity ?? 1);
            // Dark glossy parts (a visor) read as glass only if the bright room reflects faintly in them.
            if (m.color && m.color.r + m.color.g + m.color.b < 0.3) m.envMapIntensity = 0.25;
        }
    });

    bounds = new THREE.Box3().setFromObject(root);
    shadow = contactShadow(bounds);
    scene.add(shadow);

    // Bloom renders into its own target and is added on top, keeping the scene's alpha: the stock
    // bloom pass writes alpha 1 everywhere, which turned the transparent view into an opaque square.
    bloomComposer = new EffectComposer(renderer, new THREE.WebGLRenderTarget(1, 1, { type: THREE.HalfFloatType }));
    bloomComposer.renderToScreen = false;
    bloomComposer.addPass(new RenderPass(scene, camera));
    bloom = new UnrealBloomPass(new THREE.Vector2(1, 1), 0.3, 0.1, 1.2);
    bloomComposer.addPass(bloom);

    const mix = new ShaderPass(new THREE.ShaderMaterial({
        uniforms: { baseTexture: { value: null }, bloomTexture: { value: bloomComposer.renderTarget2.texture } },
        vertexShader: 'varying vec2 vUv; void main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
        fragmentShader: `uniform sampler2D baseTexture; uniform sampler2D bloomTexture; varying vec2 vUv;
            void main() {
                vec4 base = texture2D(baseTexture, vUv);
                vec3 glow = texture2D(bloomTexture, vUv).rgb;
                gl_FragColor = vec4(base.rgb + glow, clamp(base.a + max(glow.r, max(glow.g, glow.b)), 0.0, 1.0));
            }`,
    }), 'baseTexture');
    composer = new EffectComposer(renderer, new THREE.WebGLRenderTarget(1, 1, { type: THREE.HalfFloatType }));
    composer.addPass(new RenderPass(scene, camera));
    composer.addPass(mix);
    composer.addPass(new OutputPass());
    setLook(message.look ?? look);

    // Node-rig clips are additive against their first frame, so idle, gestures and poses add up as in
    // the Skia renderer. Skeletal clips (clipBlend "override") hold a whole body pose, so they replace
    // each other instead: added to the bind pose they would leave the character in a T-pose.
    const additive = bindings.clipBlend !== 'override';
    mixer = new THREE.AnimationMixer(root);
    for (const clip of gltf.animations) {
        if (additive) AnimationUtils.makeClipAdditive(clip);
        const action = mixer.clipAction(clip);
        if (additive) action.blendMode = AdditiveAnimationBlendMode;
        actions.set(clip.name, action);
    }

    resize();
    new ResizeObserver(resize).observe(document.body);
    send('loaded');
}

function dispose() {
    root?.traverse(o => {
        if (o.isMesh) {
            o.geometry.dispose();
            (Array.isArray(o.material) ? o.material : [o.material]).forEach(m => m.dispose());
        }
    });
    renderer?.domElement.remove();
    renderer?.dispose();
    renderer = scene = camera = root = mixer = undefined;
    nodes = []; baseline = []; morphMeshes = []; meshesByPrimitive = new Map(); materialsByIndex = new Map(); actions = new Map();
}

// A soft dark ellipse on the ground plane: the cheapest contact shadow, no shadow maps.
function contactShadow(box) {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 128;
    const g = canvas.getContext('2d');
    const gradient = g.createRadialGradient(64, 64, 0, 64, 64, 64);
    gradient.addColorStop(0, 'rgba(0,0,0,0.35)');
    gradient.addColorStop(1, 'rgba(0,0,0,0)');
    g.fillStyle = gradient;
    g.fillRect(0, 0, 128, 128);
    const size = box.getSize(new THREE.Vector3());
    const plane = new THREE.Mesh(new THREE.PlaneGeometry(size.x * 1.25, size.z * 1.25 + size.x * 0.25),
        new THREE.MeshBasicMaterial({ map: new THREE.CanvasTexture(canvas), transparent: true, depthWrite: false, toneMapped: false }));
    plane.rotation.x = -Math.PI / 2;
    plane.position.set((box.min.x + box.max.x) / 2, box.min.y + 0.002, (box.min.z + box.max.z) / 2);
    plane.renderOrder = -1;
    return plane;
}

function setLook(value) {
    look = value === 'basic' ? 'basic' : 'studio';
    const studio = look === 'studio';
    basicLights.forEach(l => l.visible = !studio);
    studioLights.forEach(l => l.visible = studio);
    if (scene) {
        scene.environment = studio ? environment : null;
        // The room environment is a bright white box; at full strength it washes out dark glass.
        scene.environmentIntensity = 0.45;
    }
    if (shadow) shadow.visible = studio;
    renderer.toneMapping = studio ? THREE.NeutralToneMapping : THREE.NoToneMapping;
    for (const [material, base] of emissiveBase) {
        material.emissiveIntensity = studio ? base * 1.8 : base;
        material.needsUpdate = true;
    }
}

function resize() {
    if (!renderer) return;
    const width = Math.max(1, window.innerWidth), height = Math.max(1, window.innerHeight);
    renderer.setSize(width, height, false);
    for (const c of [composer, bloomComposer]) {
        c?.setPixelRatio(renderer.getPixelRatio());
        c?.setSize(width, height);
    }
    camera.aspect = width / height;
    // Contain: a tall view keeps the model's width by widening the vertical field of view.
    const fov = bindings.framing?.verticalFov ?? 35;
    camera.fov = camera.aspect < 1 ? THREE.MathUtils.radToDeg(2 * Math.atan(Math.tan(THREE.MathUtils.degToRad(fov) / 2) / camera.aspect)) : fov;
    camera.updateProjectionMatrix();
}

function beginLayer() {
    for (const mesh of morphMeshes) before.set(mesh, mesh.morphTargetInfluences.slice());
}

function meshesFor(write) {
    return (write.primitives ?? [0]).map(p => meshesByPrimitive.get(`${write.node}:${p}`)).filter(Boolean);
}

const mouthTargets = () => new Set(bindings.expressionMouthTargets ?? []);

function applyPose(name, weight, damping = 1) {
    const writes = bindings.poses?.[name];
    if (!writes || weight <= 0) return;
    const mouth = mouthTargets();
    for (const write of writes) {
        if (write.targetIndex !== undefined) {
            const damped = write.mesh === bindings.speechMouthMesh && mouth.has(write.targetIndex) ? damping : 1;
            for (const mesh of meshesFor(write)) {
                const from = before.get(mesh)?.[write.targetIndex] ?? 0;
                mesh.morphTargetInfluences[write.targetIndex] += weight * damped * (write.value - from);
            }
        } else if (write.property) {
            applyTransform(write.node, write.property, write.value, weight);
        }
    }
}

// A pose's transform is relative to the node's rest pose, so it adds to clip motion.
const q = new THREE.Quaternion(), identity = new THREE.Quaternion(), v = new THREE.Vector3();
function applyTransform(index, property, value, weight) {
    const node = nodes[index], rest = baseline[index];
    if (!node) return;
    if (property === 'rotation') {
        q.fromArray(value).premultiply(rest.quaternion.clone().invert());
        node.quaternion.multiply(identity.clone().slerp(q, weight));
    } else if (property === 'translation') {
        node.position.add(v.fromArray(value).sub(rest.position).multiplyScalar(weight));
    } else if (property === 'scale') {
        v.fromArray(value).divide(rest.scale);
        node.scale.multiply(new THREE.Vector3(1, 1, 1).lerp(v, weight));
    }
}

function frame(f) {
    if (!renderer) return;
    const start = performance.now();

    root.position.set(0, 0, 0);
    root.scale.set(1, 1, 1);
    nodes.forEach((node, i) => {
        if (!node) return;
        node.position.copy(baseline[i].position);
        node.quaternion.copy(baseline[i].quaternion);
        node.scale.copy(baseline[i].scale);
    });
    for (const mesh of morphMeshes) mesh.morphTargetInfluences.fill(0);

    // Clips first; the blink clip is skipped where the bindings blink by morph weight.
    mixer.stopAllAction();
    // In override mode a gesture (layer 2) fades the other clips out over 0.25 s at its start and end.
    let gesture = 0;
    if (bindings.clipBlend === 'override') {
        for (const [clip, time, weight, layer] of f.c) {
            const action = actions.get(clip);
            if (layer === 2 && action) {
                const duration = action.getClip().duration;
                gesture = Math.max(gesture, weight * Math.min(1, time / 0.25, (duration - time) / 0.25));
            }
        }
    }
    for (const [clip, time, weight, layer] of f.c) {
        if (clip === 'blink' && bindings.blink) continue;
        const action = actions.get(clip);
        if (!action) continue;
        action.play();
        action.setEffectiveWeight(layer === 2 ? Math.max(gesture, 0.001) : weight * (1 - gesture));
        action.time = time;
    }
    mixer.update(0);

    beginLayer();
    for (const [pose, weight] of f.a) applyPose(pose, weight);
    beginLayer();
    const damping = 1 + (f.ms - 1) * f.sw;
    for (const [pose, weight] of f.e) applyPose(pose, weight, damping);
    beginLayer();
    for (const [pose, weight] of f.s) applyPose(pose, weight);

    if (bindings.muteBadge) for (const mesh of meshesFor(bindings.muteBadge)) mesh.morphTargetInfluences[bindings.muteBadge.targetIndex] = Math.max(mesh.morphTargetInfluences[bindings.muteBadge.targetIndex], f.m);

    if (bindings.blink) {
        for (const mesh of meshesFor(bindings.blink)) {
            for (const t of bindings.blink.suppressExpressionTargets ?? []) mesh.morphTargetInfluences[t] *= 1 - f.b;
            mesh.morphTargetInfluences[bindings.blink.targetIndex] = f.b;
        }
    }

    // A level drives a transform (min/max arrays) or a morph target (min/max numbers).
    const levelWrite = (p, level, weight) => {
        if (weight <= 0) return;
        if (p.targetIndex !== undefined) {
            const value = (p.min + (p.max - p.min) * level) * weight;
            for (const mesh of meshesFor(p)) mesh.morphTargetInfluences[p.targetIndex] = Math.max(mesh.morphTargetInfluences[p.targetIndex], value);
        } else {
            applyTransform(p.node, p.property, p.min.map((min, i) => min + (p.max[i] - min) * level), weight);
        }
    };
    for (const p of bindings.parameters?.outputLevel ?? []) levelWrite(p, f.ol, f.or);
    for (const p of bindings.parameters?.inputLevel ?? []) levelWrite(p, f.il, f.ir);

    if (bindings.gaze && nodes[bindings.gaze.node]) {
        // Gaze arrives in radians; ±12° spans the binding's range in metres.
        const range = bindings.gaze.rangeMeters ?? 0.01, full = THREE.MathUtils.degToRad(12);
        nodes[bindings.gaze.node].position.x += THREE.MathUtils.clamp(f.gx / full, -1, 1) * range;
        nodes[bindings.gaze.node].position.y += THREE.MathUtils.clamp(f.gy / full, -1, 1) * range;
    }

    // Springs from the scheduler: squash and stretch around the feet, lift, and a head tilt.
    if (f.sq || f.lf) {
        const height = bounds.max.y - bounds.min.y;
        root.scale.set(1 - f.sq * 0.6, 1 + f.sq, 1 - f.sq * 0.6);
        root.position.y = bounds.min.y * -f.sq + f.lf * height;
    }
    if (f.tl && bindings.headNode !== undefined && nodes[bindings.headNode]) nodes[bindings.headNode].rotateZ(-f.tl);

    const theme = f.d ? 'dark' : 'light';
    for (const [slot, value] of Object.entries(themes ?? {})) {
        const color = slot === 'accent' && f.ac ? f.ac : value[theme];
        for (const binding of value.bindings) {
            const match = /^material:(\d+)$/.exec(binding);
            const material = match && materialsByIndex.get(Number(match[1]));
            if (material?.color) material.color.set(color);
        }
    }

    if (look === 'studio') {
        bloomComposer.render();
        composer.render();
    }
    else renderer.render(scene, camera);
    renderMs += performance.now() - start;
    frames++;
    if (start - lastStats > 1000) {
        send(`stats:${(renderMs / frames).toFixed(3)}:${frames}`);
        renderMs = 0; frames = 0; lastStats = start;
    }
}

send('ready');
