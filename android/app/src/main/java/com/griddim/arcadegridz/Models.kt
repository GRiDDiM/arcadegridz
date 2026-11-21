package com.griddim.arcadegridz

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

const val SITE = "https://arcadegridz.com"
const val STORYBOARD_CATEGORY = "storyboard"

data class GameEntry(
    val id: String, val name: String, val category: String, val genre: String, val status: String,
    val description: String, val art: String, val tone: String, val mark: String,
    val potato: Boolean, val touch: Boolean, val playable: Boolean, val trailer: Boolean,
    val released: String, val multiplayer: String, val runtimeType: String, val url: String?,
    /** album mode: the grid shows the music albums with the same tiles */
    val album: Album? = null
) {
    val isStoryboard get() = category.equals(STORYBOARD_CATEGORY, true)
    val isLive get() = status.equals("live", true)

    companion object {
        fun parse(o: JSONObject): GameEntry {
            val rt = o.optJSONObject("runtime")
            return GameEntry(
                o.optString("id"), o.optString("name"), o.optString("category"), o.optString("genre"), o.optString("status"),
                o.optString("description"), o.optString("art"), o.optString("tone", "cyan"), o.optString("mark"),
                o.optBoolean("potato"), o.optBoolean("touch"), o.optBoolean("playable"), o.optBoolean("trailer"),
                o.optString("released"), o.optString("mp", o.optString("multiplayer")),
                rt?.optString("type", "web") ?: "web", rt?.optString("url")?.takeIf { it.isNotBlank() }
            )
        }

        fun ofAlbum(a: Album) = GameEntry(
            "album:${a.id}", a.title, "album", a.genre.uppercase(), "live", a.description, a.cover, "magenta", "",
            true, true, true, false, if (a.year > 0) a.year.toString() else "", "", "album", null, a
        )
    }
}

data class Account(val id: String, val name: String, val username: String, val avatar: String) {
    fun json(): String = JSONObject().put("id", id).put("name", name).put("username", username).put("avatar", avatar).toString()
    val initial get() = (name.firstOrNull { it.isLetterOrDigit() } ?: '?').uppercaseChar().toString()

    companion object {
        fun parse(o: JSONObject?): Account? {
            if (o == null) return null
            fun s(k: String) = if (o.isNull(k)) "" else o.optString(k)
            return Account(s("id"), s("name").ifBlank { "GRIDRUNNER" }, s("username"), s("avatar"))
        }
        fun parse(json: String?): Account? = try { if (json.isNullOrBlank()) null else parse(JSONObject(json)) } catch (_: Exception) { null }
    }
}

class CatalogResult(val games: List<GameEntry>, val offline: Boolean)

/** Same endpoint as the Windows app; ETag + on-disk cache so the launcher opens offline. */
object Catalog {
    private const val ENDPOINT = "$SITE/api/launcher/catalog"

    fun load(ctx: Context, token: String?): CatalogResult {
        val cache = File(ctx.filesDir, "catalog.json")
        val etagFile = File(ctx.filesDir, "catalog.etag")
        try {
            val etag = if (etagFile.exists() && cache.exists()) etagFile.readText() else null
            val r = Net.get(ENDPOINT, token, etag)
            if (r.code == 304 && cache.exists()) return CatalogResult(parse(cache.readText()), false)
            if (r.code !in 200..299) throw IllegalStateException("HTTP ${r.code}")
            val list = parse(r.body)
            cache.writeText(r.body)
            if (r.etag != null) etagFile.writeText(r.etag) else etagFile.delete()
            return CatalogResult(list, false)
        } catch (e: Exception) {
            log("catalog", "offline: ${e.message}")
            if (cache.exists()) return CatalogResult(parse(cache.readText()), true)
            throw e
        }
    }

    private fun parse(raw: String): List<GameEntry> {
        val arr = JSONObject(raw).optJSONArray("games") ?: JSONArray()
        return (0 until arr.length()).mapNotNull { i -> arr.optJSONObject(i)?.let { GameEntry.parse(it) } }.filter { it.id.isNotBlank() }
    }
}

data class ScoreEntry(val name: String, val score: Long, val verified: Boolean, val assisted: Boolean, val cheater: Boolean) {
    val tag get() = if (cheater) "CHEATER" else if (assisted) "ASSISTED" else if (verified) "VERIFIED" else "UNVERIFIED"
}

object Leaderboards {
    @Volatile var all: Map<String, List<ScoreEntry>> = emptyMap()
    @Volatile var loadedAt = 0L

    fun load(): Map<String, List<ScoreEntry>> {
        val r = Net.get("$SITE/api/leaderboards", timeoutMs = 7000)
        val games = JSONObject(r.body).optJSONObject("games") ?: JSONObject()
        val out = HashMap<String, List<ScoreEntry>>()
        for (k in games.keys()) {
            val arr = games.optJSONArray(k) ?: continue
            out[k] = (0 until arr.length()).mapNotNull { i ->
                arr.optJSONObject(i)?.let { ScoreEntry(it.optString("name"), it.optLong("score"), it.optBoolean("verified"), it.optBoolean("assisted"), it.optBoolean("cheater")) }
            }
        }
        all = out
        loadedAt = System.currentTimeMillis()
        return out
    }
}

data class Banner(val id: String, val game: String?, val image: String, val url: String, val alt: String)

object Banners {
    fun load(): Pair<List<Banner>, Int> {
        val o = JSONObject(Net.get("$SITE/assets/banners/banners.json").body)
        val arr = o.optJSONArray("banners") ?: JSONArray()
        val list = (0 until arr.length()).mapNotNull { i ->
            arr.optJSONObject(i)?.let { b ->
                Banner(b.optString("id"), b.optString("game").takeIf { it.isNotBlank() }, b.optString("image"), b.optString("url"), b.optString("alt"))
            }
        }.filter { it.image.isNotBlank() }
        return list to o.optInt("rotateSeconds", 30).coerceIn(8, 600)
    }
}

data class Track(val n: Int, val title: String, val file: String, val duration: Double)

data class Album(
    val id: String, val url: String, val cover: String, val genre: String, val year: Int, val description: String,
    val title: String, val subtitle: String, val artist: String, val bands: Int, val fps: Int, val specExt: String,
    var base: String = "", var tracks: List<Track> = emptyList()
)

object Albums {
    fun list(): List<Album> {
        val arr = JSONObject(Net.get("$SITE/music/albums.json").body).optJSONArray("albums") ?: JSONArray()
        return (0 until arr.length()).mapNotNull { i ->
            arr.optJSONObject(i)?.let { a ->
                val sp = a.optJSONObject("spectrum")
                Album(a.optString("id"), a.optString("url"), a.optString("cover"), a.optString("genre"), a.optInt("year"),
                    a.optString("description"), a.optString("title"), a.optString("subtitle"), a.optString("artist", "BDC"),
                    sp?.optInt("bands", 16) ?: 0, sp?.optInt("fps", 25) ?: 25, sp?.optString("ext", ".spec") ?: ".spec")
            }
        }
    }

    fun loadTracks(a: Album) {
        val o = JSONObject(Net.get(a.url).body)
        a.base = o.optString("base").ifBlank { a.url.substringBeforeLast('/') + "/" }
        val arr = o.optJSONArray("tracks") ?: JSONArray()
        a.tracks = (0 until arr.length()).mapNotNull { i ->
            arr.optJSONObject(i)?.let { Track(it.optInt("n", i + 1), it.optString("title"), it.optString("file"), it.optDouble("duration", 0.0)) }
        }
    }
}

data class DailyReward(val name: String, val amount: Long)
data class DailyQuest(val name: String, val description: String, val reward: Long, val completed: Boolean)
data class DailyState(val streak: Int, val streakBonus: Long, val balance: String, val rewards: List<DailyReward>, val quests: List<DailyQuest>) {
    val stage get() = if (streak > 0) (streak - 1) % 7 + 1 else 0
    val newSparkz get() = rewards.filter { it.amount > 0 }.sumOf { it.amount }
}

/** The Network's daily check-in, called natively (same endpoints as the Windows app and the websites). */
object Daily {
    private const val BASE = "https://griddim.com"
    private var lastUser: String? = null
    private var lastVisit = 0L

    fun visit(token: String, userId: String, force: Boolean = false): DailyState? {
        if (!force && userId == lastUser && System.currentTimeMillis() - lastVisit < 120_000) return null
        lastUser = userId; lastVisit = System.currentTimeMillis()
        val r = Net.post("$BASE/account/daily-quests/visit", JSONObject().put("site", "arcadegridz").toString(), token)
        if (r.code !in 200..299) { lastVisit = 0; return null }
        return parse(r.body)
    }

    fun state(token: String): DailyState? {
        val r = Net.get("$BASE/account/daily-quests?site=arcadegridz", token)
        return if (r.code in 200..299) parse(r.body) else null
    }

    private fun parse(raw: String): DailyState {
        val o = JSONObject(raw)
        val rw = o.optJSONArray("rewards") ?: JSONArray()
        val qs = o.optJSONArray("quests") ?: JSONArray()
        val bal = o.opt("balance")
        return DailyState(
            o.optInt("streak"), o.optLong("streakBonus"),
            if (bal is Number) "%,d".format(bal.toLong()) else "—",
            (0 until rw.length()).mapNotNull { i -> rw.optJSONObject(i)?.let { DailyReward(it.optString("name"), it.optLong("amount")) } },
            (0 until qs.length()).mapNotNull { i -> qs.optJSONObject(i)?.let { DailyQuest(it.optString("name"), it.optString("description"), it.optLong("reward"), it.optBoolean("completed")) } }
        )
    }
}

data class UpdateInfo(val version: String, val apkUrl: String, val page: String)

/** Android releases live in the same GitHub repo as the Windows app, tagged android-vX.Y.Z (never marked "latest"). */
object Updates {
    fun check(): UpdateInfo? {
        val r = Net.get("https://api.github.com/repos/GRiDDiM/arcadegridz/releases?per_page=30")
        if (r.code !in 200..299) throw IllegalStateException("GitHub HTTP ${r.code}")
        val arr = JSONArray(r.body)
        for (i in 0 until arr.length()) {
            val rel = arr.optJSONObject(i) ?: continue
            val tag = rel.optString("tag_name")
            if (!tag.startsWith("android-v") || rel.optBoolean("draft")) continue
            val assets = rel.optJSONArray("assets") ?: continue
            for (j in 0 until assets.length()) {
                val a = assets.optJSONObject(j) ?: continue
                if (a.optString("name").endsWith(".apk"))
                    return UpdateInfo(tag.removePrefix("android-v"), a.optString("browser_download_url"), rel.optString("html_url"))
            }
        }
        return null
    }

    fun newer(remote: String, local: String): Boolean {
        val a = remote.split('.').map { it.toIntOrNull() ?: 0 }
        val b = local.split('.').map { it.toIntOrNull() ?: 0 }
        for (i in 0 until maxOf(a.size, b.size)) {
            val x = a.getOrElse(i) { 0 }; val y = b.getOrElse(i) { 0 }
            if (x != y) return x > y
        }
        return false
    }
}
