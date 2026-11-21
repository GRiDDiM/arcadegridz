package com.griddim.arcadegridz

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Color
import android.net.Uri
import android.webkit.CookieManager
import android.webkit.JavascriptInterface
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
import org.json.JSONObject

/**
 * The game view: an Android WebView that runs the existing web games, exactly like WebView2 does in the Windows app.
 * The site's app bridge (apphost.js, ?host=winapp) talks to `window.chrome.webview`; a document-start shim provides
 * that object here and routes messages through [AgAndroidHost]. The app never shows the website itself: navigation
 * is limited to arcadegridz.com / griddim.com and popups are dropped.
 */
@SuppressLint("SetJavaScriptEnabled")
class GameHost(ctx: Context, private val preview: Boolean = false) {
    val view = WebView(ctx)
    var onMessage: ((String, JSONObject) -> Unit)? = null
    var onNavigate: ((String) -> Unit)? = null
    var quality = "standard"

    companion object {
        const val BRIDGE = """
(function(){
  try { window.__AG_WINAPP__ = true; window.__AG_ANDROID__ = true; document.documentElement.classList.add('ag-winapp'); } catch (e) {}
  if (window.chrome && window.chrome.webview && window.chrome.webview.__ag) return;
  var L = [];
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    __ag: true,
    postMessage: function (m) { try { AgAndroidHost.post(JSON.stringify(m)); } catch (e) {} },
    addEventListener: function (t, f) { if (t === 'message' && L.indexOf(f) < 0) L.push(f); },
    removeEventListener: function (t, f) { var i = L.indexOf(f); if (i >= 0) L.splice(i, 1); }
  };
  window.__agDeliver = function (m) { L.slice().forEach(function (f) { try { f({ data: m }); } catch (e) {} }); };
})();
"""
        // details-panel trailer previews play muted: Web Audio stays suspended, media muted, no announcer voice
        const val MUTE = """
(function(){
  try {
    var AC = window.AudioContext || window.webkitAudioContext;
    if (AC && !AC.__agMuted) {
      var M = function () { var c = new AC(...arguments); try { c.suspend(); } catch (e) {} c.resume = function () { return Promise.resolve(); }; return c; };
      M.prototype = AC.prototype; M.__agMuted = true;
      window.AudioContext = M; window.webkitAudioContext = M;
    }
    var play = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function () { this.muted = true; return play.apply(this, arguments); };
    if (window.speechSynthesis) window.speechSynthesis.speak = function () {};
  } catch (e) {}
})();
"""

        fun allowed(uri: Uri?): Boolean {
            if (uri == null) return false
            val s = uri.scheme ?: return false
            if (s == "about" || s == "data" || s == "blob") return true
            if (s != "https") return false
            val h = (uri.host ?: "").lowercase()
            return h == "arcadegridz.com" || h.endsWith(".arcadegridz.com") || h == "griddim.com" || h.endsWith(".griddim.com")
        }
    }

    init {
        view.setBackgroundColor(Color.BLACK)
        val s = view.settings
        s.javaScriptEnabled = true
        s.domStorageEnabled = true
        @Suppress("DEPRECATION")
        s.databaseEnabled = true
        s.mediaPlaybackRequiresUserGesture = false
        s.setSupportZoom(false)
        s.builtInZoomControls = false
        s.displayZoomControls = false
        s.loadWithOverviewMode = false
        s.useWideViewPort = true
        s.setSupportMultipleWindows(false)
        s.javaScriptCanOpenWindowsAutomatically = false
        s.cacheMode = WebSettings.LOAD_DEFAULT
        s.userAgentString = s.userAgentString + " ArcadeGridzAndroid/" + BuildConfig.VERSION_NAME
        view.isFocusable = true
        view.isFocusableInTouchMode = true
        view.isVerticalScrollBarEnabled = false
        view.isHorizontalScrollBarEnabled = false
        view.overScrollMode = WebView.OVER_SCROLL_NEVER
        CookieManager.getInstance().setAcceptCookie(true)
        CookieManager.getInstance().setAcceptThirdPartyCookies(view, true)

        view.addJavascriptInterface(object {
            @JavascriptInterface
            fun post(json: String) {
                mainHandler.post {
                    try {
                        val o = JSONObject(json)
                        onMessage?.invoke(o.optString("type"), o)
                    } catch (e: Exception) { log("bridge", "bad message: ${e.message}") }
                }
            }
        }, "AgAndroidHost")

        val docStart = WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT)
        if (docStart) {
            val origins = setOf("https://arcadegridz.com", "https://griddim.com", "https://*.arcadegridz.com", "https://*.griddim.com")
            WebViewCompat.addDocumentStartJavaScript(view, BRIDGE, origins)
            if (preview) WebViewCompat.addDocumentStartJavaScript(view, MUTE, origins)
        }
        log("webview", "document-start scripts=${docStart}")

        view.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(v: WebView, req: WebResourceRequest): Boolean {
                if (!req.isForMainFrame) return false
                if (!allowed(req.url)) { log("webview", "blocked ${req.url}"); return true }
                return false
            }

            override fun onPageStarted(v: WebView, url: String?, favicon: android.graphics.Bitmap?) {
                // older WebViews without document-start support: inject as early as we can
                if (!docStart) { v.evaluateJavascript(BRIDGE, null); if (preview) v.evaluateJavascript(MUTE, null) }
                if (url != null && url != "about:blank") onNavigate?.invoke(url)
            }
        }
        view.webChromeClient = object : WebChromeClient() {}
    }

    fun launch(game: GameEntry, trailer: Boolean = false, previewLoop: Boolean = false) {
        val base = game.url ?: return
        var url = base + (if (base.contains('?')) "&" else "?") + "gfx=" + Uri.encode(quality)
        if (trailer) url += "&trailer=1"
        if (previewLoop) url += "&preview=1"
        log("webview", "launch ${game.id} $url")
        view.loadUrl(url)
    }

    fun navigate(url: String) = view.loadUrl(url)

    fun send(o: JSONObject) {
        view.evaluateJavascript("window.__agDeliver&&window.__agDeliver(${o});", null)
    }

    /** Asks the page to leave the running game (EXIT failsafe). */
    fun exit() { send(JSONObject().put("type", "exit")) }

    /** Opens the site's SYSTEM MENU over the running game (same as holding SELECT+START in the page). */
    fun systemMenu() {
        view.evaluateJavascript("window.dispatchEvent(new CustomEvent('arcadegridz:system',{detail:{source:'app'}}))", null)
    }

    fun blank() { view.loadUrl("about:blank") }

    fun destroy() { try { view.stopLoading(); view.loadUrl("about:blank"); view.destroy() } catch (_: Exception) { } }
}
