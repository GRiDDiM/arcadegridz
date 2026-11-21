package com.griddim.arcadegridz

import android.app.Activity
import android.content.Intent
import android.net.Uri
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
 * Google sign-in through the phone's browser (Google blocks sign-in inside app web views), with the same PKCE +
 * loopback handoff the Windows app uses: the site redirects to http://127.0.0.1:<port>/callback with a one-time code,
 * the app exchanges it at /api/winapp/auth/exchange for a Network session token.
 */
class LoopbackAuth(private val activity: Activity) {
    @Volatile private var server: ServerSocket? = null

    private fun b64(b: ByteArray) = Base64.encodeToString(b, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
    private fun random(n: Int) = ByteArray(n).also { SecureRandom().nextBytes(it) }

    fun cancel() { try { server?.close() } catch (_: Exception) { } }

    fun start(status: (String) -> Unit, done: (AuthResult?, String?) -> Unit) {
        val verifier = b64(random(32))
        val challenge = b64(MessageDigest.getInstance("SHA-256").digest(verifier.toByteArray(Charsets.US_ASCII)))
        val state = b64(random(24))
        val socket = try { ServerSocket(0, 1, InetAddress.getByName("127.0.0.1")) } catch (e: Exception) { done(null, e.message); return }
        server = socket
        val port = socket.localPort
        val callback = "http://127.0.0.1:$port/callback"
        val authorize = "$SITE/?host=winapp&mode=desktop-signin" +
            "&challenge=${Uri.encode(challenge)}&callback=${Uri.encode(callback)}&state=${Uri.encode(state)}"
        status("OPENING GOOGLE SIGN-IN IN YOUR BROWSER...")
        try {
            activity.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(authorize)).addCategory(Intent.CATEGORY_BROWSABLE))
        } catch (e: Exception) { cancel(); done(null, "No browser found to sign in with."); return }

        Thread {
            var result: AuthResult? = null
            var error: String? = null
            try {
                socket.soTimeout = 5 * 60 * 1000
                socket.accept().use { client ->
                    if (!client.inetAddress.isLoopbackAddress) throw IllegalStateException("Rejected a non-loopback sign-in response.")
                    val reader = client.getInputStream().bufferedReader(Charsets.US_ASCII)
                    val requestLine = reader.readLine() ?: ""
                    while (true) { val l = reader.readLine(); if (l.isNullOrEmpty()) break }
                    val parts = requestLine.split(' ')
                    val target = if (parts.size >= 3 && parts[0] == "GET") parts[1] else ""
                    val query = parse(target.substringAfter('?', ""))
                    val ok = query["state"] == state && !query["code"].isNullOrBlank()
                    respond(client.getOutputStream(), ok)
                    if (query["state"] != state) throw IllegalStateException("The Google sign-in state did not match.")
                    val code = query["code"] ?: throw IllegalStateException("Google sign-in did not return a code.")
                    mainHandler.post { status("VERIFYING SECURE SIGN-IN...") }
                    val body = JSONObject().put("code", code).put("verifier", verifier).put("callback", callback).toString()
                    val r = Net.post("$SITE/api/winapp/auth/exchange", body)
                    val o = try { JSONObject(r.body) } catch (_: Exception) { JSONObject() }
                    val token = o.optString("token")
                    if (r.code !in 200..299 || !o.optBoolean("ok") || token.isBlank()) throw IllegalStateException(o.optString("error", "The sign-in exchange failed."))
                    result = AuthResult(token, o.optJSONObject("account")?.optString("name")?.ifBlank { null } ?: "GRIDRUNNER")
                }
            } catch (e: SocketTimeoutException) {
                error = "GOOGLE SIGN-IN TIMED OUT. TRY AGAIN OR CONTINUE AS GUEST."
            } catch (e: Exception) {
                error = if (socket.isClosed && result == null && e is java.net.SocketException) "CANCELLED" else (e.message ?: "Sign-in failed")
            } finally { cancel() }
            mainHandler.post { done(result, error) }
        }.start()
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
            "<main><h1>$heading</h1><p>$text</p><a href='$back'>RETURN TO ARCADEGRIDZ</a></main><script>setTimeout(function(){location.href='$back'},600)</script>"
        val bytes = html.toByteArray(Charsets.UTF_8)
        out.write(("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: ${bytes.size}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n").toByteArray(Charsets.US_ASCII))
        out.write(bytes)
        out.flush()
    }
}
