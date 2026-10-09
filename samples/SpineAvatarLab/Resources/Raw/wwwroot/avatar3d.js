// Draws a GLB avatar for AvatarView's 3D surface (issue #524, decision D1a). The C# scheduler owns
// time: every frame arrives as a message with pose, clip and level weights, and this page only
// applies them and renders. Nothing is fetched; the model arrives in the load message.
import * as THREE from 'three';
import { GLTFLoader } from './three/GLTFLoader.js';
import { AdditiveAnimationBlendMode, AnimationUtils } from 'three';

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
    scene.add(new THREE.HemisphereLight(0xf2ffff, 0x526475, 2));
    const light = new THREE.DirectionalLight(0xffffff, 3);
    light.position.set(-2, 3, 4);
    scene.add(light);

    // Clips are additive against their first frame, so idle, gestures and poses add up instead of
    // one overwriting the other: the same composition as the Skia renderer.
    mixer = new THREE.AnimationMixer(root);
    for (const clip of gltf.animations) {
        AnimationUtils.makeClipAdditive(clip);
        const action = mixer.clipAction(clip);
        action.blendMode = AdditiveAnimationBlendMode;
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

function resize() {
    if (!renderer) return;
    const width = Math.max(1, window.innerWidth), height = Math.max(1, window.innerHeight);
    renderer.setSize(width, height, false);
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

    nodes.forEach((node, i) => {
        if (!node) return;
        node.position.copy(baseline[i].position);
        node.quaternion.copy(baseline[i].quaternion);
        node.scale.copy(baseline[i].scale);
    });
    for (const mesh of morphMeshes) mesh.morphTargetInfluences.fill(0);

    // Clips first; the blink clip is skipped where the bindings blink by morph weight.
    mixer.stopAllAction();
    for (const [clip, time, weight] of f.c) {
        if (clip === 'blink' && bindings.blink) continue;
        const action = actions.get(clip);
        if (!action) continue;
        action.play();
        action.setEffectiveWeight(weight);
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

    for (const p of bindings.parameters?.outputLevel ?? []) {
        const value = p.min.map((min, i) => min + (p.max[i] - min) * f.ol);
        applyTransform(p.node, p.property, value, f.or);
    }
    for (const p of bindings.parameters?.inputLevel ?? []) {
        const value = p.min.map((min, i) => min + (p.max[i] - min) * f.il);
        applyTransform(p.node, p.property, value, f.ir);
    }

    if (bindings.gaze && nodes[bindings.gaze.node]) {
        // Gaze arrives in radians; ±12° spans the binding's range in metres.
        const range = bindings.gaze.rangeMeters ?? 0.01, full = THREE.MathUtils.degToRad(12);
        nodes[bindings.gaze.node].position.x += THREE.MathUtils.clamp(f.gx / full, -1, 1) * range;
        nodes[bindings.gaze.node].position.y += THREE.MathUtils.clamp(f.gy / full, -1, 1) * range;
    }

    const theme = f.d ? 'dark' : 'light';
    for (const [slot, value] of Object.entries(themes ?? {})) {
        const color = slot === 'accent' && f.ac ? f.ac : value[theme];
        for (const binding of value.bindings) {
            const match = /^material:(\d+)$/.exec(binding);
            const material = match && materialsByIndex.get(Number(match[1]));
            if (material?.color) material.color.set(color);
        }
    }

    renderer.render(scene, camera);
    renderMs += performance.now() - start;
    frames++;
    if (start - lastStats > 1000) {
        send(`stats:${(renderMs / frames).toFixed(3)}:${frames}`);
        renderMs = 0; frames = 0; lastStats = start;
    }
}

send('ready');
