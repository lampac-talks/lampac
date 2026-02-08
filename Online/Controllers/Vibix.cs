using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared.Models.Online.Vibix;
using Microsoft.Playwright;
using Shared.PlaywrightCore;

namespace Online.Controllers
{
    public class Vibix : BaseOnlineController
    {
        public Vibix() : base(AppInit.conf.Vibix) { }

        static readonly string[] qualityCandidates = new[] { "1080", "720", "480" };

        StreamQualityTpl buildStreamQuality(string file)
        {
            var streams = new StreamQualityTpl();
            if (string.IsNullOrEmpty(file))
                return streams;

            foreach (string q in qualityCandidates)
            {
                var g = new Regex($"{q}p?\\](\\{{[^\\}}]+\\}})?(?<file>https?://[^,\t\\[\\;\\{{ ]+)").Match(file).Groups;

                if (!string.IsNullOrEmpty(g["file"].Value))
                    streams.Append(HostStreamProxy(g["file"].Value), $"{q}p");
            }

            return streams;
        }

        [HttpGet]
        [Route("lite/vibix")]
        async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int s = -1, bool rjson = false, int voiceover = 0)
        {
            if (await IsRequestBlocked(rch: true))
                return badInitMsg;

            if (string.IsNullOrEmpty(init.token))
                return OnError();

            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            if (!hybridCache.TryGetValue($"vibix:v2:view:{kinopoisk_id}:{imdb_id}", out (Dictionary<string, JObject> seasons, string iframe) v2Cache))
            {
                v2Cache = await searchV2(imdb_id, kinopoisk_id);
                if (v2Cache.seasons != null || !string.IsNullOrEmpty(v2Cache.iframe))
                    hybridCache.Set($"vibix:v2:view:{kinopoisk_id}:{imdb_id}", v2Cache, cacheTime(40));
            }

            if (v2Cache.seasons != null)
            {
                if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
                    return OnError();

                #region Сериал
                if (s == -1)
                {
                    var tpl = new SeasonTpl(v2Cache.seasons.Count);

                    foreach (var season in v2Cache.seasons)
                    {
                        string link = $"{host}/lite/vibix?rjson={rjson}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&s={season.Key}";
                        tpl.Append($"{season.Key} сезон", link, season.Key);
                    }
                    return await ContentTpl(tpl);
                }
                else
                {
                    string sArhc = s.ToString();
                    var videos = v2Cache.seasons.First(i => i.Key == sArhc).Value["videos"].ToObject<Dictionary<string, JObject>>();
                    var etpl = new EpisodeTpl(videos.Count);

                    foreach (var video in videos)
                    {
                        string iframe = addIframeArgs(video.Value.Value<string>("iframe"));
                        etpl.Append($"{video.Key} серия", title ?? original_title, sArhc, video.Key, accsArgs($"{host}/lite/vibix/video/{AesTo.Encrypt(buildVideoPayload(iframe, null))}"), "call", vast: init.vast);
                    }
                    return await ContentTpl(etpl);
                }
                #endregion
            }

            var data = await search(imdb_id, kinopoisk_id);
            if (data == null)
                return OnError();
            if (data.type == "serial" && PlaywrightBrowser.Status != PlaywrightStatus.disabled && !string.IsNullOrEmpty(data.embed_code))
            {
                var serials = await getSerials(imdb_id, kinopoisk_id);
                if (serials?.seasons == null)
                    return OnError();

                int defaultVoiceover = data.voiceovers?.FirstOrDefault()?.id ?? 0;
                int activeVoiceover = voiceover > 0 ? voiceover : defaultVoiceover;

                VoiceTpl? vtpl = null;
                if (data.voiceovers != null && data.voiceovers.Length > 0)
                {
                    var voices = new VoiceTpl(data.voiceovers.Length);
                    foreach (var voice in data.voiceovers)
                    {
                        string link = $"{host}/lite/vibix?rjson={rjson}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&s={s}&voiceover={voice.id}";
                        voices.Append(voice.name, voice.id == activeVoiceover, link);
                    }

                    vtpl = voices;
                }

                if (s == -1)
                {
                    var tpl = new SeasonTpl(serials.seasons.Length);
                    for (int i = 0; i < serials.seasons.Length; i++)
                    {
                        int seasonNumber = i + 1;
                        string link = $"{host}/lite/vibix?rjson={rjson}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&s={seasonNumber}&voiceover={activeVoiceover}";
                        tpl.Append($"{seasonNumber} сезон", link, seasonNumber);
                    }

                    return await ContentTpl(tpl);
                }
                else
                {
                    int seasonIndex = s - 1;
                    if (seasonIndex < 0 || seasonIndex >= serials.seasons.Length)
                        return OnError();

                    var season = serials.seasons[seasonIndex];
                    var etpl = new EpisodeTpl(vtpl, season.series?.Length ?? 0);

                    if (season.series != null)
                    {
                        foreach (var episode in season.series)
                        {
                            string embed = applyEmbedArgs(data.embed_code, s.ToString(), episode.id.ToString(), activeVoiceover);
                            etpl.Append(episode.name ?? $"{episode.id} серия", title ?? original_title, s.ToString(), episode.id.ToString(), accsArgs($"{host}/lite/vibix/video/{AesTo.Encrypt(buildVideoPayload(null, embed))}"), "call", vast: init.vast);
                        }
                    }

                    return await ContentTpl(etpl);
                }
            }

            if (data.type == "movie" && PlaywrightBrowser.Status != PlaywrightStatus.disabled && !string.IsNullOrEmpty(data.iframe_url))
            {
                var mtpl = new MovieTpl(title, original_title, 1);
                string iframe = addIframeArgs(data.iframe_url);
                if (data.voiceovers != null && data.voiceovers.Length > 0)
                {
                    int defaultVoiceover = data.voiceovers.FirstOrDefault()?.id ?? 0;
                    int activeVoiceover = voiceover > 0 ? voiceover : defaultVoiceover;

                    foreach (var voice in data.voiceovers)
                    {
                        string embed = applyEmbedArgs(data.embed_code, null, null, voice.id);
                        mtpl.Append(voice.name, accsArgs($"{host}/lite/vibix/video/{AesTo.Encrypt(buildVideoPayload(iframe, embed))}") + "#.m3u8", "call", voice_name: voice.name, vast: init.vast);
                    }
                }
                else
                {
                    mtpl.Append("По-умолчанию", accsArgs($"{host}/lite/vibix/video/{AesTo.Encrypt(buildVideoPayload(iframe, data.embed_code))}") + "#.m3u8", "call", vast: init.vast);
                }
                return await ContentTpl(mtpl);
            }

            rhubFallback:
            var cache = await InvokeCacheResult<EmbedModel>(ipkey($"vibix:iframe:{data.iframe_url}"), 20, async e =>
            {

             string domain = getFrontendDomain();
             string api_url = data.iframe_url
                    .Replace("/embed/", "/api/v1/embed/")
                    .Replace("/embed-serials/", "/api/v1/embed-serials/");

                api_url += $"?iframe_url={HttpUtility.UrlEncode(data.iframe_url)}";
                api_url += $"&kp={CrypTo.unic(6).ToLower()}";
                api_url += $"&domain={domain}&parent_domain={domain}";

                var api_headers = HeadersModel.Init(
                    ("accept", "*/*"),
                    ("accept-language", "ru-RU,ru;q=0.9,uk-UA;q=0.8,uk;q=0.7,en-US;q=0.6,en;q=0.5"),
                    ("sec-fetch-dest", "empty"),
                    ("sec-fetch-mode", "cors"),
                    ("sec-fetch-site", "same-origin"),
                    ("referer", data.iframe_url)
                );

                var root = await httpHydra.Get<JObject>(api_url, addheaders: api_headers);

                if (root == null || !root.ContainsKey("data") || root["data"]?["playlist"] == null)
                    return e.Fail("root", refresh_proxy: true);

                return e.Success(new EmbedModel() { playlist = root["data"]["playlist"].ToObject<Seasons[]>() });
            });

            if (IsRhubFallback(cache))
                goto rhubFallback;

            if (data.type == "movie")
            {
                #region Фильм
                return await ContentTpl(cache, () =>
                {
                    var mtpl = new MovieTpl(title, original_title, 1);

                    foreach (var movie in cache.Value.playlist)
                    {
                        var streams = buildStreamQuality(movie.file);

                        mtpl.Append(movie.title, streams.Firts().link, streamquality: streams, vast: init.vast);
                    }

                    return mtpl;
                });
                #endregion
            }
            else
            {
                #region Сериал
                return await ContentTpl(cache, () =>
                {
                    if (s == -1)
                    {
                        var tpl = new SeasonTpl(cache.Value.playlist.Length);

                        foreach (var season in cache.Value.playlist)
                        {
                            string name = season.title;
                            if (int.TryParse(Regex.Match(name, "([0-9]+)$").Groups[1].Value, out int _s) && _s > 0)
                            {
                                string link = $"{host}/lite/vibix?rjson={rjson}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&s={_s}";
                                tpl.Append($"{_s} сезон", link, _s);
                            }
                        }

                        return tpl;
                    }
                    else
                    {
                        var etpl = new EpisodeTpl();
                        string sArhc = s.ToString();

                        foreach (var season in cache.Value.playlist)
                        {
                            if (!season.title.EndsWith($" {s}"))
                                continue;

                            foreach (var episode in season.folder)
                            {
                                string name = episode.title;
                                string file = episode.folder?.First().file ?? episode.file;

                                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(file))
                                    continue;

                                var streams = buildStreamQuality(file);

                                etpl.Append(name, title ?? original_title, sArhc, Regex.Match(name, "([0-9]+)").Groups[1].Value, streams.Firts().link, streamquality: streams, vast: init.vast);
                            }
                        }

                        return etpl;
                    }
                });
                #endregion
            }
        }
         string addIframeArgs(string iframe)
        {
            if (string.IsNullOrEmpty(iframe))
                return iframe;

            if (iframe.Contains("domain=") || iframe.Contains("parent_domain="))
                return iframe;

            string delimiter = iframe.Contains("?") ? "&" : "?";
            string domain = getFrontendDomain();
            return $"{iframe}{delimiter}domain={domain}&parent_domain={domain}";
        }

        string buildVideoPayload(string iframe, string embed)
        {
            if (string.IsNullOrEmpty(embed))
                return iframe;

            return JObject.FromObject(new
            {
                iframe,
                embed
            }).ToString();
        }

        string buildEmbedHtml(string embedCode)
        {
            if (string.IsNullOrEmpty(embedCode))
                return null;

            embedCode = normalizeEmbedCode(embedCode);

            return $@"<!doctype html>
<html>
  <head>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <script src=""https://graphicslab.io/sdk/v2/rendex-sdk.min.js""></script>
  </head>
  <body>
    {embedCode}
  </body>
</html>";
        }

        string applyEmbedArgs(string embedCode, string season, string episodes, int voiceover)
        {
            if (string.IsNullOrEmpty(embedCode))
                return embedCode;

            embedCode = normalizeEmbedCode(embedCode);
            var attrs = new List<string>();

            if (!string.IsNullOrEmpty(season) && !embedCode.Contains("data-season="))
                attrs.Add($"data-season=\"{season}\"");

            if (!string.IsNullOrEmpty(episodes) && !embedCode.Contains("data-episodes="))
                attrs.Add($"data-episodes=\"{episodes}\"");

            if (voiceover > 0 && !embedCode.Contains("data-voiceover="))
                attrs.Add($"data-voiceover=\"{voiceover}\"");

            if (voiceover > 0 && !embedCode.Contains("data-voiceover-only="))
                attrs.Add("data-voiceover-only=\"true\"");

            if (attrs.Count == 0)
                return embedCode;

            string insert = $"<ins {string.Join(" ", attrs)} ";
            return Regex.Replace(embedCode, "<ins\\b", insert, RegexOptions.IgnoreCase);
        }

        string normalizeEmbedCode(string embedCode)
        {
            if (string.IsNullOrEmpty(embedCode))
                return embedCode;

            if (Regex.IsMatch(embedCode, "<ins\\b", RegexOptions.IgnoreCase))
                return embedCode;

            return $"<ins {embedCode}></ins>";
        }

        #region Video
        [HttpGet]
        [Route("lite/vibix/video/{*iframe}")]
        async public ValueTask<ActionResult> Video(string iframe)
        {
            if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
                return OnError();

            if (await IsRequestBlocked(rch: true))
                return badInitMsg;

            string decrypted = AesTo.Decrypt(iframe);
            if (string.IsNullOrEmpty(decrypted))
                return OnError();

            string embedCode = null;
            string iframeUrl = decrypted;

            if (decrypted.StartsWith("{"))
            {
                try
                {
                    var payload = JObject.Parse(decrypted);
                    iframeUrl = payload.Value<string>("iframe");
                    embedCode = payload.Value<string>("embed");
                }
                catch { }
            }

            iframeUrl = addIframeArgs(iframeUrl);
            if (string.IsNullOrEmpty(iframeUrl) && string.IsNullOrEmpty(embedCode))
                return OnError();

            string cacheKey = iframeUrl ?? CrypTo.md5(embedCode ?? string.Empty);
            return await InvkSemaphore($"vibix:video:{cacheKey}:{proxyManager?.CurrentProxyIp}", async key =>
            {
                if (!hybridCache.TryGetValue(key, out (string location, StreamQualityTpl streamquality) cache))
                {
                    int bestQuality = 0;
                    var streamquality = new StreamQualityTpl();
                    string referer = Regex.Match(iframeUrl ?? string.Empty, "(^https?://[^/]+)").Groups[1].Value;
                    if (string.IsNullOrEmpty(referer))
                        referer = $"https://{getFrontendDomain()}";
                    var headers = httpHeaders(init, HeadersModel.Init
                    (
                        ("referer", referer)
                    ));

                        TimeSpan? cacheTtl = null;
                    try
                    {
                        using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                        {
                            var page = await browser.NewPageAsync(init.plugin, proxy: proxy_data, headers: headers?.ToDictionary()).ConfigureAwait(false);
                            if (page == null)
                                return OnError();

                            await page.AddInitScriptAsync(@"() => {
                                try {
                                    localStorage.setItem('pljsquality', '1080p');
                                    localStorage.setItem('ksquality', '1080p');
                                } catch (e) {}
                            }").ConfigureAwait(false);

                            await page.RouteAsync("**/api/v1/embed/**", async route =>
                            {
                                try
                                {
                                    if (route.Request.Method != "GET")
                                    {
                                        await route.ContinueAsync();
                                        return;
                                    }

                                    await route.ContinueAsync();
                                }
                                catch
                                {
                                    await route.ContinueAsync();
                                }
                            });

                             await page.RouteAsync("**/*", async route =>
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(cache.location))
                                    {
                                        await route.AbortAsync();
                                        return;
                                    }

                                    var reqUrl = route.Request.Url;

                                    if (reqUrl.Contains("/hls/", StringComparison.OrdinalIgnoreCase) &&
                                        reqUrl.Contains("/seg-", StringComparison.OrdinalIgnoreCase) &&
                                        reqUrl.Contains(".ts", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var playlist = Regex.Replace(reqUrl, @"/seg-[^/?]+\.ts", "/index.m3u8", RegexOptions.IgnoreCase);
                                        updateCacheTtl(ref cacheTtl, reqUrl);
                                        updateLocation(ref cache.location, ref bestQuality, playlist, streamquality);
                                    }

                                    if (reqUrl.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        (reqUrl.IndexOf(".mp4", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                         reqUrl.IndexOf(".ts", StringComparison.OrdinalIgnoreCase) < 0))
                                    {
                                        updateCacheTtl(ref cacheTtl, reqUrl);
                                        updateLocation(ref cache.location, ref bestQuality, reqUrl, streamquality);
                                    }

                                    if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true))
                                        return;

                                    await route.ContinueAsync();
                                }
                                catch { }
                            });

                            var options = new PageGotoOptions()
                            {
                                Timeout = 15_000,
                                WaitUntil = WaitUntilState.NetworkIdle
                            };

                            IResponse result = null;

                            try
                            {
                                await page.GotoAsync("https://cm.vibix.biz", new PageGotoOptions()
                                {
                                    Timeout = 10_000,
                                    WaitUntil = WaitUntilState.DOMContentLoaded
                                }).ConfigureAwait(false);
                            }
                            catch { }

                            if (!string.IsNullOrEmpty(embedCode))
                            {
                                try
                                {
                                    await page.SetContentAsync(buildEmbedHtml(embedCode), new PageSetContentOptions()
                                    {
                                        Timeout = 15_000,
                                        WaitUntil = WaitUntilState.NetworkIdle
                                    }).ConfigureAwait(false);
                                }
                                catch { }
                            }
                            else if (!string.IsNullOrEmpty(iframeUrl))
                            {
                                try
                                {
                                    await page.SetContentAsync(PlaywrightBase.IframeHtml(iframeUrl), new PageSetContentOptions()
                                    {
                                        Timeout = 15_000,
                                        WaitUntil = WaitUntilState.NetworkIdle
                                    }).ConfigureAwait(false);
                                }
                                catch { }
                            }

                            if (string.IsNullOrEmpty(cache.location) && !string.IsNullOrEmpty(iframeUrl))
                                result = await page.GotoAsync(iframeUrl, options).ConfigureAwait(false);
                            if (result != null)
                            {
                                try
                                {
                                    await page.EvaluateAsync(@"() => {
                                        const video = document.querySelector('video');
                                        if (video) {
                                            video.muted = true;
                                            video.play().catch(() => {});
                                        }
                                        const btn = document.querySelector('.vjs-big-play-button, .plyr__control--overlaid, .jw-icon-playback, .jw-icon-play');
                                        if (btn) btn.click();
                                    }").ConfigureAwait(false);
                                }
                                catch { }
                            }

                            if (string.IsNullOrEmpty(cache.location))
                                await page.WaitForTimeoutAsync(3000).ConfigureAwait(false);

                            if (result != null && string.IsNullOrEmpty(cache.location))
                            {
                                string html = await page.ContentAsync().ConfigureAwait(false);
                                cache.location = Regex.Match(html, "<video preload=\"none\" src=\"(https?://[^\"]+)\"").Groups[1].Value;
                                if (!cache.location.Contains(".m3u") && !cache.location.Contains(".mp4"))
                                    cache.location = null;
                            }

                            PlaywrightBase.WebLog("SET", iframe, cache.location, proxy_data);
                        }

                        if (string.IsNullOrEmpty(cache.location))
                        {
                            proxyManager?.Refresh();
                            return OnError();
                        }
                    }
                    catch
                    {
                        return OnError();
                    }

                    proxyManager?.Success();
                    cache.streamquality = streamquality;
                    hybridCache.Set(key, cache, resolveCacheTtl(cacheTtl, cacheTime(20)));
                }

                string refererStream = Regex.Match(iframeUrl ?? string.Empty, "(^https?://[^/]+)").Groups[1].Value;
                if (string.IsNullOrEmpty(refererStream))
                    refererStream = "https://cm.vibix.biz";
                var headers_stream = httpHeaders(init.corsHost(), HeadersModel.Join(HeadersModel.Init("referer", refererStream), init.headers_stream));

                string link = HostStreamProxy(cache.location, headers: headers_stream);
                var streamQuality = cache.streamquality;

                bool hasQualities = false;
                try
                {
					if (streamQuality.Any())
						hasQualities = true;
                }
                catch { }

                if (!hasQualities)
                    return ContentTo(VideoTpl.ToJson("play", link, "auto", vast: init.vast));

                return ContentTo(VideoTpl.ToJson("play", streamQuality.Firts().link, "auto", streamquality: streamQuality, vast: init.vast));
            });
        }
        #endregion

        void updateLocation(ref string location, ref int bestQuality, string candidate, StreamQualityTpl streamquality)
        {
            if (string.IsNullOrEmpty(candidate))
                return;

            int q = getQualityFromUrl(candidate);
            if (q == 0)
            {
                if (tryExpandQualityVariants(candidate, streamquality, ref bestQuality, ref location))
                    return;

                if (string.IsNullOrEmpty(location))
                    location = candidate;
                return;
            }

            streamquality.Append(HostStreamProxy(candidate), $"{q}p");

            if (q > bestQuality)
            {
                bestQuality = q;
                location = candidate;
            }
        }

        bool tryExpandQualityVariants(string candidate, StreamQualityTpl streamquality, ref int bestQuality, ref string location)
        {
            var match = Regex.Match(candidate, "/hls/\\d+/\\d+/(?<id>\\d+)\\.mp4/index\\.m3u8", RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            string id = match.Groups["id"].Value;
            int endIdx = candidate.IndexOf($"{id}.mp4/index.m3u8", StringComparison.OrdinalIgnoreCase);
            if (endIdx < 0)
                return false;

            string prefix = candidate.Substring(0, endIdx);
            string suffix = candidate.Substring(endIdx + $"{id}.mp4/index.m3u8".Length);

            foreach (int quality in new[] { 1080, 720, 480 })
            {
                string variant = $"{prefix}{id}_{quality}p.mp4/index.m3u8{suffix}";
                streamquality.Append(HostStreamProxy(variant), $"{quality}p");

                if (quality > bestQuality)
                {
                    bestQuality = quality;
                    location = variant;
                }
            }

            return true;
        }

        int getQualityFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return 0;

            var match = Regex.Match(url, "(2160|1440|1080|720|480|360|240)p", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int q))
                return q;

            return 0;
        }

        void updateCacheTtl(ref TimeSpan? cacheTtl, string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return;

            var query = HttpUtility.ParseQueryString(uri.Query);
            if (!long.TryParse(query["expires"], out long expires))
                return;

            DateTimeOffset expiresAt;
            try
            {
                expiresAt = expires > 9999999999 ? DateTimeOffset.FromUnixTimeMilliseconds(expires) : DateTimeOffset.FromUnixTimeSeconds(expires);
            }
            catch
            {
                return;
            }

            var ttl = expiresAt - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(45);
            if (ttl <= TimeSpan.Zero)
                return;

            if (ttl < TimeSpan.FromSeconds(30))
                ttl = TimeSpan.FromSeconds(30);

            if (!cacheTtl.HasValue || ttl < cacheTtl.Value)
                cacheTtl = ttl;
        }

        TimeSpan resolveCacheTtl(TimeSpan? cacheTtl, TimeSpan fallback)
        {
            if (!cacheTtl.HasValue)
                return fallback;

            if (cacheTtl.Value <= TimeSpan.Zero)
                return TimeSpan.FromSeconds(1);

            return cacheTtl.Value < fallback ? cacheTtl.Value : fallback;
        }

        string getFrontendDomain()
        {
            if (Uri.TryCreate(host, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                return uri.Host;

            if (Uri.TryCreate($"https://{host}", UriKind.Absolute, out var fallback) && !string.IsNullOrEmpty(fallback.Host))
                return fallback.Host;

            return "cm.vibix.biz";
        }

        #region search
        async ValueTask<Video> search(string imdb_id, long kinopoisk_id)
        {
            string memKey = $"vibix:view:{kinopoisk_id}:{imdb_id}";

            if (!hybridCache.TryGetValue(memKey, out Video root))
            {
                root = await goSearch(null, kinopoisk_id) ?? await goSearch(imdb_id, 0);
                if (root == null)
                    return null;

                proxyManager?.Success();
                hybridCache.Set(memKey, root, cacheTime(30));
            }

            return root;
        }

        async Task<Video> goSearch(string imdb_id, long kinopoisk_id)
        {
            if (string.IsNullOrEmpty(imdb_id) && kinopoisk_id == 0)
                return null;

            string uri = kinopoisk_id > 0 ? $"kp/{kinopoisk_id}" : $"imdb/{imdb_id}";

            var video = await httpHydra.Get<Video>($"{init.host}/api/v1/publisher/videos/{uri}", safety: true, addheaders: HeadersModel.Init(
                ("Accept", "application/json"),
                ("Authorization", $"Bearer {init.token}"),
                ("X-CSRF-TOKEN", "")
            ));

            if (video == null)
            {
                proxyManager?.Refresh();
                return null;
            }

            if (string.IsNullOrEmpty(video.type))
                return null;

            if (string.IsNullOrEmpty(video.iframe_url) || !video.iframe_url.Contains("token="))
            {
                string iframe = getIframeFromEmbed(video.embed_code);
                if (!string.IsNullOrEmpty(iframe))
                    video.iframe_url = iframe;
            }

            if (string.IsNullOrEmpty(video.iframe_url))
                return null;

            return video;
        }
        #endregion

        #region serials
        async Task<SerialsRoot> getSerials(string imdb_id, long kinopoisk_id)
        {
            if (string.IsNullOrEmpty(imdb_id) && kinopoisk_id == 0)
                return null;

            string uri = kinopoisk_id > 0 ? $"kp/{kinopoisk_id}" : $"imdb/{imdb_id}";

            var serials = await httpHydra.Get<SerialsRoot>($"{init.host}/api/v1/serials/{uri}", safety: true, addheaders: HeadersModel.Init(
                ("Accept", "application/json"),
                ("Authorization", $"Bearer {init.token}"),
                ("X-CSRF-TOKEN", "")
            ));

            if (serials?.seasons == null)
            {
                proxyManager?.Refresh();
                return null;
            }

            proxyManager?.Success();
            return serials;
        }
        #endregion

        string getIframeFromEmbed(string embed)
        {
            if (string.IsNullOrEmpty(embed))
                return null;

            string iframe = Regex.Match(embed, "src=[\"'](?<url>https?://[^\"']+)", RegexOptions.IgnoreCase).Groups["url"].Value;
            if (!string.IsNullOrEmpty(iframe))
                return iframe;

            return null;
        }

        #region searchV2
        async Task<(Dictionary<string, JObject> seasons, string iframe)> searchV2(string imdb_id, long kinopoisk_id)
        {
            string imdbArg = !string.IsNullOrEmpty(imdb_id) ? $"&imdb={imdb_id}" : null;
            string kpArg = kinopoisk_id > 0 ? $"&kp={kinopoisk_id}" : null;

            JToken data = null;
            foreach (var candidate in new (string item, string arg)[] { ("movie", kpArg), ("movie", imdbArg), ("serial", kpArg), ("serial", imdbArg) })
            {
                data = await goSearchV2(candidate.item, candidate.arg);
                if (data != null)
                    break;
            }

            if (data == null)
                return (null, null);

            string iframe = data.Value<string>("iframe");
            var seasons = data["seasons"]?.ToObject<Dictionary<string, JObject>>();

            if (string.IsNullOrEmpty(iframe) && seasons == null)
                return (null, null);

            proxyManager?.Success();
            return (seasons, iframe);
        }

        async Task<JToken> goSearchV2(string item, string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return null;

            long kp = 0;
            string imdb = null;
            if (arg.StartsWith("&kp=", StringComparison.OrdinalIgnoreCase))
                long.TryParse(arg[4..], out kp);
            else if (arg.StartsWith("&imdb=", StringComparison.OrdinalIgnoreCase))
                imdb = arg[6..];

            var video = await goSearch(imdb, kp);
            if (video == null || !string.Equals(video.type, item, StringComparison.OrdinalIgnoreCase))
                return null;

            if (string.IsNullOrEmpty(video.iframe_url))
                return null;

            return new JObject
            {
                ["iframe"] = video.iframe_url
            };
        }
        #endregion
    }
}
