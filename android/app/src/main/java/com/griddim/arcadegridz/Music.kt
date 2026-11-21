package com.griddim.arcadegridz

import android.content.Context
import android.graphics.Canvas
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Shader
import android.media.AudioAttributes
import android.media.MediaPlayer
import android.view.View

/** BDC's albums (music/albums.json), streamed with MediaPlayer. Same controls as the Windows app's bottom bar. */
class Music(private val settings: Settings) {
    var albums: List<Album> = emptyList(); private set
    var album: Album? = null; private set
    var index = 0; private set
    var onChange: (() -> Unit)? = null
    private var player: MediaPlayer? = null
    private var prepared = false
    private val spec = HashMap<String, ByteArray>()
    var playing = false; private set
    /** paused by the app (a game is running / app in background) rather than by the player */
    private var held = false

    val track: Track? get() = album?.tracks?.getOrNull(index)
    val position: Int get() = try { if (prepared) player?.currentPosition ?: 0 else 0 } catch (_: Exception) { 0 }
    val duration: Int get() = try { if (prepared) player?.duration ?: 0 else 0 } catch (_: Exception) { 0 }

    fun load(autoplay: Boolean) {
        bg({
            val list = Albums.list()
            val a = list.firstOrNull { it.id == settings.musicAlbum } ?: list.firstOrNull()
            if (a != null) Albums.loadTracks(a)
            list to a
        }) { r ->
            if (r == null) return@bg
            albums = r.first
            album = r.second
            index = settings.musicTrack.coerceIn(0, maxOf(0, (album?.tracks?.size ?: 1) - 1))
            onChange?.invoke()
            if (autoplay) play(index)
        }
    }

    fun selectAlbum(a: Album, start: Boolean = true) {
        bg({ if (a.tracks.isEmpty()) Albums.loadTracks(a); a }) { loaded ->
            if (loaded == null) return@bg
            album = loaded
            settings.musicAlbum = loaded.id
            index = 0
            if (start) play(0) else onChange?.invoke()
        }
    }

    fun play(i: Int) {
        val a = album ?: return
        if (a.tracks.isEmpty()) return
        index = ((i % a.tracks.size) + a.tracks.size) % a.tracks.size
        settings.musicTrack = index
        val t = a.tracks[index]
        release()
        val mp = MediaPlayer()
        player = mp
        prepared = false
        mp.setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
        try { mp.setDataSource(a.base + t.file) } catch (e: Exception) { log("music", "source ${e.message}"); return }
        mp.setOnPreparedListener {
            prepared = true
            applyVolume()
            if (!held) { it.start(); playing = true }
            onChange?.invoke()
        }
        mp.setOnCompletionListener { next(auto = true) }
        mp.setOnErrorListener { _, w, x -> log("music", "error $w/$x"); playing = false; onChange?.invoke(); true }
        mp.prepareAsync()
        playing = true
        loadSpec(a, t)
        onChange?.invoke()
    }

    fun toggle() {
        val mp = player
        if (mp == null || !prepared) { if (album != null) play(index) else load(true); return }
        if (mp.isPlaying) { mp.pause(); playing = false } else { mp.start(); playing = true }
        onChange?.invoke()
    }

    fun next(auto: Boolean = false) {
        val a = album ?: return
        if (a.tracks.isEmpty()) return
        when {
            auto && settings.musicRepeat == 2 -> play(index)
            settings.musicShuffle && a.tracks.size > 1 -> { var n: Int; do { n = (Math.random() * a.tracks.size).toInt() } while (n == index); play(n) }
            auto && settings.musicRepeat == 0 && index == a.tracks.size - 1 -> { playing = false; onChange?.invoke() }
            else -> play(index + 1)
        }
    }

    fun prev() { if (position > 4000) seekTo(0) else play(index - 1) }

    fun seekTo(ms: Int) { try { if (prepared) player?.seekTo(ms) } catch (_: Exception) { } }

    fun applyVolume() {
        val v = if (settings.musicMuted) 0f else settings.musicVolume
        try { player?.setVolume(v, v) } catch (_: Exception) { }
    }

    /** Games and the background pause the album; it resumes where it was. */
    fun hold(on: Boolean) {
        held = on
        val mp = player ?: return
        try {
            if (on) { if (mp.isPlaying) mp.pause() }
            else if (playing && prepared && !mp.isPlaying) mp.start()
        } catch (_: Exception) { }
    }

    fun release() {
        try { player?.release() } catch (_: Exception) { }
        player = null; prepared = false
    }

    private fun loadSpec(a: Album, t: Track) {
        if (a.bands <= 0) return
        val key = a.id + "/" + t.file
        if (spec.containsKey(key)) return
        val url = a.base + t.file.substringBeforeLast('.') + a.specExt
        bg({ Net.bytes(url) }) { data -> if (data != null && data.size >= a.bands) spec[key] = data }
    }

    /** Visualizer bands 0..1 for the current position, false when there is no spectrum data. */
    fun bands(out: FloatArray): Boolean {
        val a = album ?: return false
        val t = track ?: return false
        val data = spec[a.id + "/" + t.file] ?: return false
        if (!playing || settings.musicMuted || !prepared) { out.fill(0f); return true }
        val frames = data.size / a.bands
        val f = (position / 1000.0 * a.fps).toInt().coerceIn(0, maxOf(0, frames - 1))
        for (i in out.indices) {
            val b = (i * a.bands / out.size).coerceIn(0, a.bands - 1)
            out[i] = (data[f * a.bands + b].toInt() and 0xFF) / 255f
        }
        return true
    }
}

/** Neon spectrum bars driven by the album's precomputed .spec data (falls back to a gentle idle wave). */
class SpectrumView(ctx: Context, private val music: Music, private val count: Int = 16) : View(ctx) {
    private val p = Paint(Paint.ANTI_ALIAS_FLAG)
    private val target = FloatArray(count)
    private val level = FloatArray(count)
    private val r = RectF()

    override fun onDraw(c: Canvas) {
        val t = Themes.current
        val has = music.bands(target)
        if (!has) { val s = System.currentTimeMillis() / 400.0; for (i in 0 until count) target[i] = if (music.playing) (0.25 + 0.2 * Math.sin(s + i * 0.7)).toFloat() else 0.04f }
        val w = width.toFloat(); val h = height.toFloat()
        val gap = w / count * 0.25f
        val bw = w / count - gap
        p.shader = LinearGradient(0f, h, 0f, 0f, intArrayOf(t.cyan, t.violet, t.magenta), null, Shader.TileMode.CLAMP)
        for (i in 0 until count) {
            level[i] += (target[i] - level[i]) * (if (target[i] > level[i]) 0.55f else 0.18f)
            val bh = (h * level[i]).coerceAtLeast(h * 0.06f)
            val x = i * (bw + gap)
            r.set(x, h - bh, x + bw, h)
            c.drawRoundRect(r, bw / 3f, bw / 3f, p)
        }
        p.shader = null
        if (isShown) postInvalidateDelayed(40)
    }
}
