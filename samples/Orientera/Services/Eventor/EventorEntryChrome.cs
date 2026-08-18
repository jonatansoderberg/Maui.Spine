namespace Orientera.Services.Eventor;

/// <summary>
/// What is run inside Eventor's entry page so that the form is the thing on the screen.
/// </summary>
/// <remarks>
/// The heaviest finding in the test run was not that the app hands over to Eventor — that is the
/// right call, and the entry stays valid because of it — but that everything the app says about
/// itself disappears at the moment that decides whether a runner competes. What arrived was an ad
/// banner, a house ad, Eventor's own menu and a cookie bar, with the form itself well below the
/// fold and five more ad blocks under it.
/// <para>
/// The page is not ours to change, so nothing here rewrites it. The chrome is hidden and the
/// content column is allowed to use the width; everything Eventor does — the form, its rules, its
/// payment, its confirmation — is untouched. The selectors are read off the live page rather than
/// guessed: <c>#topMenuContainer</c> is the sponsor strip, <c>#header</c> the logo band,
/// <c>#middleMenu</c> the yellow bar, <c>#leeads-panorama-outer-1</c> the banner above the
/// content, and <c>#adSideBar</c> the column of house ads that lands under the form on a phone.
/// </para>
/// <para>
/// Hiding them is not enough on its own. <c>#grid</c> is a CSS grid declared
/// <c>width:1343px; grid-template-columns:1012px 331px</c> with explicit rows, so a hidden child
/// leaves its row standing and the content drops below a screenful of white — and that fixed width
/// is the same thing that pushed the form wider than the phone, which is how the bib number ended
/// up cut off at the right edge. The grid is therefore turned back into a plain block that takes
/// the width it is given.
/// </para>
/// </remarks>
public static class EventorEntryChrome
{
    /// <summary>
    /// Hides Eventor's own chrome and lets the content column have the screen. Runs on every
    /// navigation: the entry flow moves between pages, and each one arrives with the chrome back.
    /// </summary>
    public const string HideChrome =
        "(function(){var i='orientera-chrome';if(document.getElementById(i))return 'kept';" +
        "var s=document.createElement('style');s.id=i;s.textContent=" +
        "'#topMenuContainer,#header,#middleMenu,#adSideBar,#footer," +
        ".sponsorbox,.a-d-area,.a-d-item,.adInfoLink,.DivToBecomeSticky," +
        // The ad script builds and moves its own elements after the page has loaded, so hiding the
        // container it started in is not enough — the sticky banner is reparented out of it. Matched
        // on the prefix its ids and classes all carry instead.
        "[id^=leeads],[class*=leeads]{display:none!important}" +
        "#grid{display:block!important;width:auto!important;max-width:100%!important;" +
        "grid-template-columns:none!important;grid-template-rows:none!important;margin:0!important}" +
        "#content,#main{display:block!important;width:auto!important;max-width:100%!important;" +
        "margin:0!important;padding:12px!important;float:none!important}" +
        "body{overflow-x:hidden!important}" +
        "table,form,input,select{max-width:100%!important}';" +
        "document.head.appendChild(s);return 'hidden';})()";

    /// <summary>
    /// Picks the class the runner chose in the app, if Eventor's form offers it.
    /// </summary>
    /// <remarks>
    /// Matched on what the option says rather than on the field's name. The name is not measurable
    /// from outside — the form is behind the login — and the visible text is what the runner
    /// compared against anyway when the test run found the box standing on "Insk. 2,0" while the
    /// app had just shown "Blå 3,5". A miss leaves the form exactly as Eventor served it, which is
    /// why the landing screen says which class it is sending: an unfulfilled promise is then
    /// visible instead of silent.
    /// </remarks>
    public static string SelectClass(string className) =>
        "(function(c){if(!c)return 'none';var t=function(x){return (x||'').replace(/\\s+/g,' ').trim();};" +
        "var ss=document.querySelectorAll('select');" +
        "for(var i=0;i<ss.length;i++){var o=ss[i].options;for(var j=0;j<o.length;j++){" +
        "if(t(o[j].text)===t(c)){ss[i].selectedIndex=j;" +
        "ss[i].dispatchEvent(new Event('change',{bubbles:true}));return 'set';}}}" +
        "return 'missing';})(" + Quote(className) + ")";

    private static string Quote(string value) =>
        "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
}
