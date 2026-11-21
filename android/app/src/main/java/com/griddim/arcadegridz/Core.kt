package com.griddim.arcadegridz

import android.content.Context
import android.content.SharedPreferences
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.media.AudioAttributes
import android.media.SoundPool
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.util.LruCache
import android.widget.ImageView
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.util.concurrent.Executors

val mainHandler = Handler(Looper.getMainLooper())
val io = Executors.newFixedThreadPool(4)

fun log(tag: String, msg: String) = Log.i("ArcadeGridz", "[$tag] $msg")

/** Runs [work] off the UI thread and hands the result back on it. */
fun <T> bg(work: () -> T, done: (T?) -> Unit) {
    io.execute {
        val r = try { work() } catch (e: Exception) { log("bg", "${e.javaClass.simpleName}: ${e.message}"); null }
        mainHandler.post { done(r) }
    }
}

/** Launcher settings (SharedPreferences). Mirrors the Windows SettingsService where it applies to phones. */
class Settings(ctx: Context) {
    private val p: SharedPreferences = ctx.getSharedPreferences("arcadegridz", Context.MODE_PRIVATE)
    private fun b(k: String, d: Boolean): Boolean = p.getBoolean(k, d)
    private fun put(k: String, v: Boolean) { p.edit().putBoolean(k, v).apply() }

    var showAll: Boolean
        get() = b("showAll", false)
        set(v) { put("showAll", v) }

    var showTesting: Boolean
        get() = b("showTesting", false)
        set(v) { put("showTesting", v) }

    var theme: String
        get() = p.getString("theme", "neon") ?: "neon"
        set(v) { p.edit().putString("theme", v).apply() }

    var hideScanlines: Boolean
        get() = b("hideScanlines", false)
        set(v) { put("hideScanlines", v) }

    var stillBackground: Boolean
        get() = b("stillBackground", false)
        set(v) { put("stillBackground", v) }

    var noTrailerPreview: Boolean
        get() = b("noTrailerPreview", false)
        set(v) { put("noTrailerPreview", v) }

    var musicOff: Boolean
        get() = b("musicOff", false)
        set(v) { put("musicOff", v) }

    var musicMuted: Boolean
        get() = b("musicMuted", false)
        set(v) { put("musicMuted", v) }

    var musicShuffle: Boolean
        get() = b("musicShuffle", false)
        set(v) { put("musicShuffle", v) }

    var musicRepeat: Int
        get() = p.getInt("musicRepeat", 1)
        set(v) { p.edit().putInt("musicRepeat", v).apply() }

    var musicVolume: Float
        get() = p.getFloat("musicVolume", 0.25f)
        set(v) { p.edit().putFloat("musicVolume", v).apply() }

    var musicTrack: Int
        get() = p.getInt("musicTrack", 0)
        set(v) { p.edit().putInt("musicTrack", v).apply() }

    var musicAlbum: String
        get() = p.getString("musicAlbum", "game-over") ?: "game-over"
        set(v) { p.edit().putString("musicAlbum", v).apply() }

    var sfxOff: Boolean
        get() = b("sfxOff", false)
        set(v) { put("sfxOff", v) }

    var quality: String
        get() = p.getString("quality", "standard") ?: "standard"
        set(v) { p.edit().putString("quality", v).apply() }

    var orientation: String
        get() = p.getString("orientation", "auto") ?: "auto"
        set(v) { p.edit().putString("orientation", v).apply() }

    var skippedUpdate: String
        get() = p.getString("skippedUpdate", "") ?: ""
        set(v) { p.edit().putString("skippedUpdate", v).apply() }

    var token: String?
        get() = p.getString("token", null)
        set(v) { p.edit().putString("token", v).apply() }

    var accountJson: String?
        get() = p.getString("account", null)
        set(v) { p.edit().putString("account", v).apply() }

    var guest: Boolean
        get() = b("guest", false)
        set(v) { put("guest", v) }
}

class HttpResult(val code: Int, val body: String, val etag: String?)

object Net {
    var userAgent = "ArcadeGridz-Android"

    private fun open(url: String, method: String, token: String?, timeoutMs: Int): HttpURLConnection {
        val c = URL(url).openConnection() as HttpURLConnection
        c.requestMethod = method
        c.connectTimeout = timeoutMs
        c.readTimeout = timeoutMs
        c.setRequestProperty("User-Agent", userAgent)
        c.setRequestProperty("Cache-Control", "no-store")
        if (!token.isNullOrBlank()) c.setRequestProperty("Authorization", "Bearer $token")
        return c
    }

    fun get(url: String, token: String? = null, etag: String? = null, timeoutMs: Int = 9000): HttpResult {
        val c = open(url, "GET", token, timeoutMs)
        if (etag != null) c.setRequestProperty("If-None-Match", etag)
        try {
            val code = c.responseCode
            val stream = if (code in 200..299) c.inputStream else c.errorStream
            val body = stream?.bufferedReader()?.use { it.readText() } ?: ""
            return HttpResult(code, body, c.getHeaderField("ETag"))
        } finally { c.disconnect() }
    }

    fun post(url: String, json: String, token: String? = null, timeoutMs: Int = 15000): HttpResult {
        val c = open(url, "POST", token, timeoutMs)
        c.doOutput = true
        c.setRequestProperty("Content-Type", "application/json")
        try {
            c.outputStream.use { it.write(json.toByteArray()) }
            val code = c.responseCode
            val stream = if (code in 200..299) c.inputStream else c.errorStream
            return HttpResult(code, stream?.bufferedReader()?.use { it.readText() } ?: "", null)
        } finally { c.disconnect() }
    }

    fun bytes(url: String, timeoutMs: Int = 15000): ByteArray {
        val c = open(url, "GET", null, timeoutMs)
        try {
            if (c.responseCode !in 200..299) throw IllegalStateException("HTTP ${c.responseCode}")
            return c.inputStream.use { it.readBytes() }
        } finally { c.disconnect() }
    }
}

/** Menu sounds: the same wavs the Windows app ships (assets/audio). */
object Sfx {
    private var pool: SoundPool? = null
    private val ids = HashMap<String, Int>()
    var enabled = true
    private var lastMove = 0L

    fun init(ctx: Context, on: Boolean) {
        enabled = on
        if (pool != null) return
        val sp = SoundPool.Builder().setMaxStreams(4).setAudioAttributes(
            AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_GAME).setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build()
        ).build()
        pool = sp
        for (name in listOf("move", "select", "back", "open", "launch", "error", "jingle")) {
            try { ctx.assets.openFd("audio/$name.wav").use { ids[name] = sp.load(it, 1) } } catch (_: Exception) { }
        }
    }

    fun play(name: String) {
        if (!enabled) return
        val now = System.currentTimeMillis()
        if (name == "move") { if (now - lastMove < 60) return; lastMove = now }
        val id = ids[name] ?: return
        val vol = if (name == "move") 0.35f else 0.6f
        pool?.play(id, vol, vol, 1, 0, 1f)
    }
}

/** Cover-art loader: memory LRU + disk cache, decoded down to [maxPx]. */
object Images {
    private val mem = object : LruCache<String, Bitmap>((Runtime.getRuntime().maxMemory() / 8).toInt()) {
        override fun sizeOf(key: String, value: Bitmap) = value.byteCount
    }
    private var dir: File? = null
    fun init(ctx: Context) { dir = File(ctx.cacheDir, "art").apply { mkdirs() } }

    private fun key(url: String): String =
        MessageDigest.getInstance("SHA-1").digest(url.toByteArray()).joinToString("") { "%02x".format(it) }

    fun load(url: String?, view: ImageView?, maxPx: Int = 900, done: ((Bitmap?) -> Unit)? = null) {
        if (url.isNullOrBlank()) { view?.setImageDrawable(null); done?.invoke(null); return }
        val k = "$url@$maxPx"
        view?.tag = k
        mem.get(k)?.let { view?.setImageBitmap(it); done?.invoke(it); return }
        io.execute {
            val bmp = try { fetch(url, maxPx) } catch (e: Exception) { log("art", "$url ${e.message}"); null }
            if (bmp != null) mem.put(k, bmp)
            mainHandler.post {
                if (view != null && view.tag == k) view.setImageBitmap(bmp)
                done?.invoke(bmp)
            }
        }
    }

    private fun fetch(url: String, maxPx: Int): Bitmap? {
        val f = dir?.let { File(it, key(url)) }
        val data = if (f != null && f.exists() && System.currentTimeMillis() - f.lastModified() < 3L * 24 * 3600 * 1000) f.readBytes()
        else Net.bytes(url).also { b -> try { f?.writeBytes(b) } catch (_: Exception) { } }
        val o = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(data, 0, data.size, o)
        var sample = 1
        while (o.outWidth / (sample * 2) >= maxPx || o.outHeight / (sample * 2) >= maxPx) sample *= 2
        return BitmapFactory.decodeByteArray(data, 0, data.size, BitmapFactory.Options().apply { inSampleSize = sample })
    }
}
