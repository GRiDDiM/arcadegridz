package com.griddim.arcadegridz

import android.app.Activity
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.IBinder
import android.util.Base64
import org.json.JSONObject
import java.net.InetAddress
import java.net.ServerSocket
import java.net.SocketTimeoutException
import java.net.URLDecoder
import java.security.MessageDigest
import java.security.SecureRandom

class AuthResult(val token: String, val name: String)

/**
 * Google sign-in through the phone's browser (Google blocks sign-in inside app web views), with the same PKCE handoff
 * the Windows app uses (/api/winapp/auth/code -> /api/winapp/auth/exchange).
 * The one-time code comes back either to a loopback listener (http://127.0.0.1:<port>/callback, kept alive by a
 * short foreground service so Android doesn't freeze the app while the browser is open) or, when the site supports it
 * (&app=android), through an arcadegridz://auth?code=..&state=.. intent. Verifier/state are saved so the return still
 * works if Android restarted the app meanwhile.
 */
class LoopbackAuth(private val activity: Activity) {
    @Volatile private var server: ServerSocket? = null
    @Volatile private var finished = false
    private var verifier = ""
    private var state = ""
    private var callback = ""
    var onStatus: ((String) -> Unit)? = null
    var onDone: ((AuthResult?, String?) -> Unit)? = null

    private fun b64(b: ByteArray) = Base64.encodeToString(b, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
    private fun random(n: Int) = ByteArray(n).also { SecureRandom().nextBytes(it) }
    private val prefs get() = activity.getSharedPreferences("arcadegridz-auth", Context.MODE_PRIVATE)

    fun cancel() {
        try { server?.close() } catch (_: Exception) { }
        server = null
        KeepAlive.stop(activity)
    }

    /** Restores a pending sign-in after Android restarted the app. */
    fun resumeFromPrefs(@Suppress("UNUSED_PARAMETER") settings: Settings) {
        verifier = prefs.getString("verifier", "") ?: ""
        state = prefs.getString("state", "") ?: ""
        callback = prefs.getString("callback", "") ?: ""
    }

    fun start() {
        finished = false
        verifier = b64(random(32))
        val challenge = b64(MessageDigest.getInstance("SHA-256").digest(verifier.toByteArray(Charsets.US_ASCII)))
        state = b64(random(24))
        val socket = try { ServerSocket(0, 1, InetAddress.getByName("127.0.0.1")) } catch (e: Exception) { finish(null, e.message); return }
        server = socket
        callback = "http://127.0.0.1:${socket.localPort}/callback"
        prefs.edit().putString("verifier", verifier).putString("state", state).putString("callback", callback)
            .putLong("at", System.currentTimeMillis()).apply()
        val authorize = "$SITE/?host=winapp&mode=desktop-signin&app=android" +
            "&challenge=${Uri.encode(challenge)}&callback=${Uri.encode(callback)}&state=${Uri.encode(state)}"
        onStatus?.invoke("FINISH SIGNING IN WITH GOOGLE IN YOUR BROWSER...")
        KeepAlive.start(activity)
        try {
            // open as a Chrome Custom Tab when available (stays in this app's task), plain browser otherwise
            val i = Intent(Intent.ACTION_VIEW, Uri.parse(authorize)).addCategory(Intent.CATEGORY_BROWSABLE)
            i.putExtras(Bundle().apply { putBinder("android.support.customtabs.extra.SESSION", null) })
            i.putExtra("android.support.customtabs.extra.TOOLBAR_COLOR", 0xFF07051A.toInt())
            activity.startActivity(i)
        } catch (e: Exception) { cancel(); finish(null, "No browser found to sign in with."); return }

        Thread {
            try {
                socket.soTimeout = 10 * 60 * 1000
                socket.accept().use { client ->
                    if (!client.inetAddress.isLoopbackAddress) throw IllegalStateException("Rejected a non-loopback sign-in response.")
                    val reader = client.getInputStream().bufferedReader(Charsets.US_ASCII)
                    val requestLine = reader.readLine() ?: ""
                    while (true) { val l = reader.readLine(); if (l.isNullOrEmpty()) break }
                    val parts = requestLine.split(' ')
                    val target = if (parts.size >= 3 && parts[0] == "GET") parts[1] else ""
                    val query = parse(target.substringAfter('?', ""))
                    respond(client.getOutputStream(), query["state"] == state && !query["code"].isNullOrBlank())
                    val code = query["code"]
                    val st = query["state"]
                    mainHandler.post { if (code != null && st != null) completeWithCode(code, st) else finish(null, "Google sign-in did not return a code.") }
                }
            } catch (e: SocketTimeoutException) {
                mainHandler.post { if (!finished) finish(null, "GOOGLE SIGN-IN TIMED OUT. TRY AGAIN OR CONTINUE AS GUEST.") }
            } catch (_: Exception) {
                // closed: the intent return finished the sign-in, or it was cancelled
            }
        }.start()
    }

    /** Exchanges the one-time code (from the loopback listener or the arcadegridz:// return) for a session token. */
    fun completeWithCode(code: String, returnedState: String) {
        if (finished) return
        if (state.isBlank() || returnedState != state) { finish(null, "The Google sign-in state did not match. Try again."); return }
        val savedAt = prefs.getLong("at", 0)
        if (System.currentTimeMillis() - savedAt > 15 * 60 * 1000) { finish(null, "That sign-in expired. Try again."); return }
        finished = true
        try { server?.close() } catch (_: Exception) { }
        onStatus?.invoke("VERIFYING SECURE SIGN-IN...")
        val body = JSONObject().put("code", code).put("verifier", verifier).put("callback", callback).toString()
        bg({ Net.post("$SITE/api/winapp/auth/exchange", body) }) { r ->
            val o = try { JSONObject(r?.body ?: "") } catch (_: Exception) { JSONObject() }
            val token = o.optString("token")
            if (r == null || r.code !in 200..299 || !o.optBoolean("ok") || token.isBlank()) {
                finished = false
                finish(null, o.optString("error").ifBlank { "The sign-in exchange failed." })
            } else {
                finish(AuthResult(token, o.optJSONObject("account")?.optString("name")?.ifBlank { null } ?: "GRIDRUNNER"), null)
            }
        }
    }

    private fun finish(result: AuthResult?, error: String?) {
        finished = true
        prefs.edit().clear().apply()
        cancel()
        onDone?.invoke(result, error)
    }

    private fun parse(q: String): Map<String, String> = q.split('&').filter { it.isNotEmpty() }.associate {
        val kv = it.split('=', limit = 2)
        URLDecoder.decode(kv[0], "UTF-8") to URLDecoder.decode(kv.getOrElse(1) { "" }, "UTF-8")
    }

    private fun respond(out: java.io.OutputStream, ok: Boolean) {
        val heading = if (ok) "YOU'RE SIGNED IN" else "SIGN-IN FAILED"
        val text = if (ok) "ArcadeGridz received the secure sign-in. Head back to the app." else "Go back to ArcadeGridz and try again."
        val back = "intent://signed-in#Intent;scheme=arcadegridz;package=${activity.packageName};end"
        val html = "<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>ArcadeGridz</title>" +
            "<style>html{color-scheme:dark}body{margin:0;min-height:100vh;display:grid;place-items:center;background:#050313;color:#fff;font-family:system-ui,sans-serif}" +
            "main{max-width:560px;margin:20px;padding:36px;text-align:center;border:3px solid #55e8ff;border-radius:24px;background:#0c0923}h1{color:#55e8ff;letter-spacing:.08em}" +
            "p{line-height:1.6;color:#d7d7eb}a{display:inline-block;margin-top:14px;padding:16px 26px;border-radius:14px;background:linear-gradient(90deg,#3aff9a,#5ee7ff);color:#07051a;font-weight:800;text-decoration:none;letter-spacing:.1em}</style>" +
            "<main><h1>$heading</h1><p>$text</p><a href='$back'>RETURN TO ARCADEGRIDZ</a></main>"
        val bytes = html.toByteArray(Charsets.UTF_8)
        out.write(("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: ${bytes.size}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n").toByteArray(Charsets.US_ASCII))
        out.write(bytes)
        out.flush()
        // bring the app back on its own; the page above is the fallback
        mainHandler.post {
            try { activity.startActivity(Intent(activity, activity.javaClass).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT or Intent.FLAG_ACTIVITY_NEW_TASK)) } catch (_: Exception) { }
        }
    }
}

/** Short foreground service that keeps the app awake (not frozen) while Google sign-in runs in the browser. */
class KeepAlive : Service() {
    companion object {
        fun start(ctx: Context) {
            try {
                val i = Intent(ctx, KeepAlive::class.java)
                if (Build.VERSION.SDK_INT >= 26) ctx.startForegroundService(i) else ctx.startService(i)
            } catch (e: Exception) { log("auth", "keep-alive unavailable: ${e.message}") }
        }
        fun stop(ctx: Context) { try { ctx.stopService(Intent(ctx, KeepAlive::class.java)) } catch (_: Exception) { } }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val nm = getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26) nm.createNotificationChannel(NotificationChannel("signin", "Sign-in", NotificationManager.IMPORTANCE_LOW))
        val n = (if (Build.VERSION.SDK_INT >= 26) Notification.Builder(this, "signin") else @Suppress("DEPRECATION") Notification.Builder(this))
            .setContentTitle("ArcadeGridz")
            .setContentText("Waiting for Google sign-in...")
            .setSmallIcon(android.R.drawable.stat_sys_download)
            .setOngoing(true)
            .build()
        try {
            if (Build.VERSION.SDK_INT >= 34) startForeground(7, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
            else startForeground(7, n)
        } catch (e: Exception) { log("auth", "foreground refused: ${e.message}") }
        // never linger: sign-in either finishes or gives up within ten minutes
        mainHandler.postDelayed({ stopSelf() }, 10 * 60 * 1000L)
        return START_NOT_STICKY
    }
}
