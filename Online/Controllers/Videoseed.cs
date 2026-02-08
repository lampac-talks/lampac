using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using System.Linq;
using Newtonsoft.Json.Linq;
using Shared.PlaywrightCore;

namespace Online.Controllers
{
    public class Videoseed : BaseOnlineController
    {
        public Videoseed() : base(AppInit.conf.Videoseed) { }

        static int ExtractOrderNumber(string key)
        {
            string numericKey = Regex.Replace(key ?? string.Empty, "\\D", string.Empty);
            return int.TryParse(numericKey, out int n) ? n : int.MaxValue;
        }

        string GetEscapedToken()
        {
            if (string.IsNullOrEmpty(init.token))
                return null;

            return Uri.EscapeDataString(NormalizeToken(init.token));
        }

        [HttpGet]
        [Route("lite/videoseed")]
		async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int year, int s = -1, bool rjson = false, int serial = -1, string t = null)        {
            if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
                return OnError();

            if (await IsRequestBlocked(rch: false))
                return badInitMsg;

            if (string.IsNullOrEmpty(init.token))
                return OnError();

            return await InvkSemaphore($"videoseed:view:{kinopoisk_id}:{imdb_id}:{original_title}", async key =>
            {
                #region search
                if (!hybridCache.TryGetValue(key, out (Dictionary<string, JObject> seasons, string iframe, Dictionary<string, string> translations) cache))
                {
                    var candidates = new (bool ok, string arg)[]
                    {
                        (kinopoisk_id > 0, $"&kp={kinopoisk_id}"),
                        (!string.IsNullOrEmpty(imdb_id), $"&imdb={imdb_id}"),
                        (!string.IsNullOrEmpty(imdb_id), $"&imdb_id={imdb_id}"),
                        (!string.IsNullOrEmpty(imdb_id), $"&tmdb={imdb_id}"),
                        (!string.IsNullOrEmpty(original_title), $"&q={HttpUtility.UrlEncode(original_title)}&release_year_from={year - 1}&release_year_to={year + 1}")
                    };

                    JToken data = null;
                    foreach (var c in candidates)
                    {
                        data = await goSearch(serial, c.ok, c.arg);
                        if (data != null)
                            break;
                    }

                    if (data == null)
                    {
                        proxyManager?.Refresh();
                        return OnError();
                    }
					
                    cache.translations = data?["translation_iframe"]?.ToObject<Dictionary<string, JObject>>()
                        ?.ToDictionary(item => item.Key, item => item.Value?.Value<string>("iframe"));

                    if (serial == 1)
                        cache.seasons = data?["seasons"]?.ToObject<Dictionary<string, JObject>>();
                    else
					{
                        cache.iframe = data?.Value<string>("iframe");
                    }

                    if (cache.seasons == null && string.IsNullOrEmpty(cache.iframe))
                    {
                        proxyManager?.Refresh();
                        return OnError();
                    }

                    proxyManager?.Success();
                    hybridCache.Set(key, cache, cacheTime(40));
                }
                #endregion

                if (cache.iframe != null)
                {
                    #region Фильм
                    var mtpl = new MovieTpl(title, original_title, 1);
                    mtpl.Append("По-умолчанию", accsArgs($"{host}/lite/videoseed/video/{AesTo.Encrypt(cache.iframe)}") + "#.m3u8", "call", vast: init.vast);
                    
					if (cache.translations != null)
                    {
                        foreach (var translation in cache.translations)
                        {
                            if (string.IsNullOrEmpty(translation.Value) || translation.Value == cache.iframe)
                                continue;

                            mtpl.Append(translation.Key, accsArgs($"{host}/lite/videoseed/video/{AesTo.Encrypt(translation.Value)}") + "#.m3u8", "call", voice_name: translation.Key, vast: init.vast);
                        }
                    }

                    return await ContentTpl(mtpl);
                    #endregion
                }
                else
                {
                    #region Сериал
                    string enc_title = HttpUtility.UrlEncode(title);
                    string enc_original_title = HttpUtility.UrlEncode(original_title);

                    if (s == -1)
                    {
                        var tpl = new SeasonTpl(cache.seasons.Count);

                        foreach (var season in cache.seasons
                            .OrderBy(item => ExtractOrderNumber(item.Key))
                            .ThenBy(item => item.Key))
                        {
                            string link = $"{host}/lite/videoseed?rjson={rjson}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&s={season.Key}";
                            tpl.Append($"{season.Key} сезон", link, season.Key);
                        }

                        return await ContentTpl(tpl);
                    }
                    else
                    {
                        string sArhc = s.ToString();
                        string activeTranslation = t;
                        bool hasTranslations = false;
                        VoiceTpl vtpl = default;

                        if (cache.translations?.Count > 0)
                        {
                            vtpl = new VoiceTpl(cache.translations.Count);
                            hasTranslations = true;
                            foreach (var translation in cache.translations)
                            {
                                if (string.IsNullOrWhiteSpace(activeTranslation))
                                    activeTranslation = translation.Key;

                                string link = $"{host}/lite/videoseed?rjson={rjson}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&s={s}&t={HttpUtility.UrlEncode(translation.Key)}";
                                vtpl.Append(translation.Key, activeTranslation == translation.Key, link);
                            }
                        }
						
                        var videos = cache.seasons.First(i => i.Key == sArhc).Value["videos"].ToObject<Dictionary<string, JObject>>();

                         var etpl = hasTranslations ? new EpisodeTpl(vtpl, videos.Count) : new EpisodeTpl(videos.Count);

                        string defaultAudio = null;
                        if (!string.IsNullOrWhiteSpace(activeTranslation) && cache.translations != null && cache.translations.TryGetValue(activeTranslation, out string translationIframe))
                            defaultAudio = ExtractDefaultAudio(translationIframe);

                        foreach (var video in videos
                            .OrderBy(item => ExtractOrderNumber(item.Key))
                            .ThenBy(item => item.Key))
                        {
                            string iframe = video.Value.Value<string>("iframe");

                            if (!string.IsNullOrEmpty(defaultAudio))
                                iframe = ApplyDefaultAudio(iframe, defaultAudio);

							etpl.Append($"{video.Key} серия", title ?? original_title, sArhc, video.Key, accsArgs($"{host}/lite/videoseed/video/{AesTo.Encrypt(iframe)}"), "call", vast: init.vast);
                        }

                        return await ContentTpl(etpl);
                    }
                    #endregion
                }
            });
        }

        #region Video
        [HttpGet]
        [Route("lite/videoseed/video/{*iframe}")]
        async public ValueTask<ActionResult> Video(string iframe)
        {
            if (await IsRequestBlocked(rch: false))
                return badInitMsg;

            iframe = AesTo.Decrypt(iframe);
            if (string.IsNullOrEmpty(iframe))
                return OnError();

            var tokenValue = GetEscapedToken();
            if (!string.IsNullOrEmpty(tokenValue))
            {
                if (iframe.Contains("token=", StringComparison.OrdinalIgnoreCase))
                    iframe = Regex.Replace(iframe, "token=[^&]+", $"token={tokenValue}", RegexOptions.IgnoreCase);
                else
                    iframe = $"{iframe}{(iframe.Contains('?') ? "&" : "?")}token={tokenValue}";
            }

            iframe = NormalizeIframeParams(iframe);

            return await InvkSemaphore($"videoseed:video:{iframe}:{proxyManager?.CurrentProxyIp}", async key =>
            {
				const string hardUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";
				
                string iframeHost = Regex.Match(iframe, "(^https?://[^/]+)").Groups[1].Value;
                if (string.IsNullOrEmpty(iframeHost))
                    iframeHost = init.host;
				
				var proxyInfo = proxyManager?.BaseGet() ?? default;

                if (!hybridCache.TryGetValue(key, out string location))
                {
                    var headers = httpHeaders(init);
					if (headers != null)
                        headers.Add(new HeadersModel("User-Agent", hardUserAgent));
                    else
                        headers = HeadersModel.Init(("User-Agent", hardUserAgent));

                    string gotoReferer = string.IsNullOrEmpty(iframeHost) ? init.host : iframeHost;
                    if (!string.IsNullOrEmpty(gotoReferer) && !gotoReferer.EndsWith("/"))
                        gotoReferer += "/";

                    try
                    {
                        using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                        {
                            var page = await browser.NewPageAsync(init.plugin, proxy: proxyInfo.data, headers: headers?.ToDictionary()).ConfigureAwait(false);
                            if (page == null)
                                return OnError();

                            await page.AddInitScriptAsync("localStorage.setItem('pljsquality', '1080p');").ConfigureAwait(false);

                            var locationFinal = false;

                            page.Response += (_, response) =>
                            {
                                try
                                {
                                    string url = response.Url;
                                    if (!IsMediaUrl(url))
                                        return;

                                    if (response.Status == 200)
                                    {
                                      location = url;
                                        locationFinal = true;
                                    }
                                    else if (response.Status == 301 || response.Status == 302)
                                    {
                                        if (response.Headers.TryGetValue("location", out string redirect))
                                        {
                                            location = ResolveRedirectUrl(url, redirect);
                                            locationFinal = false;
                                        }
                                    }
                                }
                                catch { }
                            };
							
							await page.RouteAsync("**/*", async route =>
                            {
								try
                                {
                                    var rt = route.Request.ResourceType;
                                    if (rt == "image" || rt == "font" || rt == "stylesheet")
                                    {
                                        await route.AbortAsync();
                                        return;
                                    }

                                    await route.ContinueAsync();
                                }
                                catch { }
                            });

                            var waitMedia = page.WaitForResponseAsync(r => IsMediaUrl(r.Url), new() { Timeout = 15_000 });

                            var options = new PageGotoOptions()
                            {
                                Timeout = 15_000,
                                WaitUntil = WaitUntilState.DOMContentLoaded,
                                Referer = gotoReferer
                            };

                            var result = await page.GotoAsync(iframe, options).ConfigureAwait(false);

                            PlaywrightBase.WebLog("GOTO", iframe, result != null ? result.Status.ToString() : "null", proxy_data);

                            try
                            {
                                await page.EvaluateAsync(@"() => {
                                    const v = document.querySelector('video');
                                    if (v) {
                                        try { v.muted = true; } catch(e) {}
                                        try { v.play && v.play(); } catch(e) {}
                                    }
                                    const btn = document.querySelector('.vjs-big-play-button, .jw-icon-playback, .plyr__control--overlaid, .playerjs-play, .playerjs__play');
                                    if (btn) { try { btn.click(); } catch(e) {} }
                                    try { document.body && document.body.click(); } catch(e) {}
                                }");

                                await page.Mouse.ClickAsync(20, 20);
                                await page.Keyboard.PressAsync("Space");
                            }
                            catch { }

                            try
                            {
                                var mediaResp = await waitMedia.ConfigureAwait(false);
                                if (mediaResp != null && string.IsNullOrEmpty(location))
                                {
									if (mediaResp.Status == 301 || mediaResp.Status == 302)
                                    {
                                        if (mediaResp.Headers.TryGetValue("location", out var redirect))
                                            location = ResolveRedirectUrl(mediaResp.Url, redirect);
                                        locationFinal = false;
                                    }
                                    else
                                    {
                                        location = mediaResp.Url;
                                        locationFinal = mediaResp.Status == 200;
                                    }
                                }
                            }
                            catch { }

                            for (int i = 0; i < 40 && !locationFinal; i++)
                                await Task.Delay(250).ConfigureAwait(false);
                            if (result != null && string.IsNullOrEmpty(location))
                            {
                                string html = await page.ContentAsync().ConfigureAwait(false);
                                                                location = Regex.Match(html, "<source[^>]+src=\"(https?://[^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;

                                if (string.IsNullOrEmpty(location))
                                    location = Regex.Match(html, "<video[^>]+src=\"(https?://[^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;

                                if (string.IsNullOrEmpty(location))
                                    location = Regex.Match(html, "(https?:\\\\/\\\\/[^\"'\\s]+\\.m3u8[^\"'\\s]*)", RegexOptions.IgnoreCase).Groups[1].Value;

                                if (!string.IsNullOrEmpty(location))
                                {
                                    location = location.Replace("\\/", "/");

                                    if (location.StartsWith("//"))
                                    {
                                        var iframeUri = new Uri(iframe);
                                        location = $"{iframeUri.Scheme}:{location}";
                                    }
                                    else if (location.StartsWith("/"))
                                    {
                                        var iframeUri = new Uri(iframe);
                                        location = new Uri(iframeUri, location).ToString();
                                    }
                                }

                                if (string.IsNullOrEmpty(location) || (!location.Contains(".m3u") && !location.Contains(".mp4")))
                                    location = null;
                            }

                            PlaywrightBase.WebLog("SET", iframe, location, proxy_data);
                        }

                        if (string.IsNullOrEmpty(location))
                        {
                            proxyManager?.Refresh();
                            return OnError();
                        }
                    }
                    catch (Exception ex)
                    {
                        PlaywrightBase.WebLog("ERR", iframe, $"{ex.GetType().Name}: {ex.Message}", proxy_data);
                        return OnError();
                    }

                    proxyManager?.Success();
                    hybridCache.Set(key, location, cacheTime(20));
                }

                string streamReferer = string.IsNullOrEmpty(iframeHost) ? iframe : (iframeHost.EndsWith("/") ? iframeHost : iframeHost + "/");
                var headers_stream = HeadersModel.Join(
                    HeadersModel.Init(
                        ("User-Agent", hardUserAgent),
                        ("Referer", streamReferer),
                        ("Origin", iframeHost),
                        ("Accept", "*/*")
                    ),
                    init.headers_stream
                );

                string link = HostStreamProxy(init, location, headers_stream, proxyInfo.proxy, force_streamproxy: true, rch);

                if (!string.IsNullOrEmpty(location) && location.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                    link += "#.m3u8";

                return ContentTo(VideoTpl.ToJson("play", link, "auto", vast: init.vast));
            });
        }
        #endregion

        #region goSearch
        async Task<JToken> goSearch(int serial, bool isOk, string arg)
        {
            if (!isOk)
                return null;

            string apiHost = init.apihost ?? init.host;
			string tokenValue = GetEscapedToken() ?? "";
            var root = await httpHydra.Get<JObject>(
                $"{init.cors(apiHost)}/apiv2.php?item={(serial == 1 ? "serial" : "movie")}&token={tokenValue}" + arg,
                safety: true,
                addheaders: HeadersModel.Init(
                    ("referer", apiHost),
                    ("origin", apiHost)
                ));
            if (root == null || !root.ContainsKey("data") || root.Value<string>("status") == "error")
            {
                proxyManager?.Refresh();
                return null;
            }

            return root["data"]?.First;
        }
        #endregion
		
        static string NormalizeIframeParams(string iframe)
        {
            if (string.IsNullOrEmpty(iframe))
                return iframe;

            return Regex.Replace(iframe, "default_audio=([^&]+)", match =>
            {
                string value = match.Groups[1].Value;
                if (string.IsNullOrEmpty(value) || value.Contains("%"))
                    return match.Value;

                return $"default_audio={Uri.EscapeDataString(value)}";
            }, RegexOptions.IgnoreCase);
        }

        static string NormalizeToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return token;

            int separatorIndex = token.LastIndexOf('|');
            if (separatorIndex >= 0 && separatorIndex < token.Length - 1)
                return token[(separatorIndex + 1)..];

            return token;
        }
        static string ExtractDefaultAudio(string iframe)
        {
            if (string.IsNullOrEmpty(iframe))
                return null;

            if (Uri.TryCreate(iframe, UriKind.Absolute, out var uri))
            {
                var query = HttpUtility.ParseQueryString(uri.Query);
                return query.Get("default_audio");
            }

            var match = Regex.Match(iframe, "default_audio=([^&]+)", RegexOptions.IgnoreCase);
            return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
        }

        static string ApplyDefaultAudio(string iframe, string defaultAudio)
        {
            if (string.IsNullOrEmpty(iframe) || string.IsNullOrEmpty(defaultAudio))
                return iframe;

            string encoded = Uri.EscapeDataString(defaultAudio);
            if (iframe.Contains("default_audio=", StringComparison.OrdinalIgnoreCase))
                return Regex.Replace(iframe, "default_audio=[^&]+", $"default_audio={encoded}", RegexOptions.IgnoreCase);

            return $"{iframe}{(iframe.Contains('?') ? "&" : "?")}default_audio={encoded}";
        }

        static bool IsMediaUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains(".m3u8") || (url.Contains(".mp4") && !url.Contains(".ts"));
        }

        static string ResolveRedirectUrl(string requestUrl, string redirect)
        {
            if (string.IsNullOrEmpty(redirect))
                return redirect;

            if (redirect.StartsWith("//", StringComparison.Ordinal))
            {
                var requestUri = new Uri(requestUrl);
                return $"{requestUri.Scheme}:{redirect}";
            }

            if (redirect.StartsWith("/", StringComparison.Ordinal))
            {
                var requestUri = new Uri(requestUrl);
                return new Uri(requestUri, redirect).ToString();
            }

            return redirect;
        }
    }
}