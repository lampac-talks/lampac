using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using System;
using System.Linq;
using System.Text;
using System.Web;
using IO = System.IO;

namespace Lampac.Controllers
{
    public class ApiController : BaseController
    {
        #region Version / Headers / geo / myip / reqinfo
        [HttpGet]
        [AllowAnonymous]
        [Route("/version")]
        public ActionResult Version() => string.IsNullOrEmpty(versionTag)
            ? Content($"{appversion}.{minorversion}")
            : Content($"{appversion}.{minorversion}-{versionTag}");

        [HttpGet]
        [AllowAnonymous]
        [Route("/ping")]
        public ActionResult PingPong() => Content("pong");

        [HttpGet]
        [AllowAnonymous]
        [Route("/headers")]
        public ActionResult Headers(string type)
        {
            if (type == "text")
            {
                return Content(string.Join(
                    Environment.NewLine,
                    HttpContext.Request.Headers.Select(h => $"{h.Key}: {h.Value}")
                ));
            }

            return Json(HttpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()));
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("/geo")]
        public ActionResult Geo(string select, string ip)
        {
            if (select == "ip")
                return Content(ip ?? requestInfo.IP);

            string country = requestInfo.Country;
            if (ip != null)
                country = GeoIP2.Country(ip);

            if (select == "country")
                return Content(country);

            return Json(new
            { 
                ip = ip ?? requestInfo.IP,
                country
            });
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("/myip")]
        public ActionResult MyIP() => Content(requestInfo.IP);

        [HttpGet]
        [Route("/reqinfo")]
        public ActionResult Reqinfo() => ContentTo(JsonConvert.SerializeObject(requestInfo, new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        }));
        #endregion


        #region invc-ws.js
        [HttpGet]
        [AllowAnonymous]
        [Route("invc-ws.js")]
        [Route("invc-ws/js/{token}")]
        public ActionResult InvcSyncJS(string token)
        {
            StringBuilder sb;

            if (AppInit.conf.sync_user.version == 1)
            {
                sb = new StringBuilder(FileCache.ReadAllText("plugins/invc-ws.js"));
            }    
            else
            {
                sb = new StringBuilder(FileCache.ReadAllText("plugins/sync_v2/invc-ws.js"));
            }

            sb.Replace("{invc-rch}", FileCache.ReadAllText("plugins/invc-rch.js"))
              .Replace("{invc-rch_nws}", FileCache.ReadAllText("plugins/invc-rch_nws.js"))
              .Replace("{localhost}", host)
              .Replace("{token}", HttpUtility.UrlEncode(token));

            return Content(sb.ToString(), "application/javascript; charset=utf-8");
        }
        #endregion

        #region invc-rch.js
        [HttpGet]
        [AllowAnonymous]
        [Route("invc-rch.js")]
        public ActionResult InvcRchJS()
        {
            string source = FileCache.ReadAllText("plugins/invc-rch.js").Replace("{localhost}", host);

            source = $"(function(){{'use strict'; {source} }})();";

            return Content(source, "application/javascript; charset=utf-8");
        }
        #endregion


        #region nws-client-es5.js
        [HttpGet]
        [AllowAnonymous]
        [Route("nws-client-es5.js")]
        [Route("js/nws-client-es5.js")]
        public ActionResult NwsClient()
        {
            string memKey = "ApiController:nws-client-es5.js";
            if (!memoryCache.TryGetValue(memKey, out string source))
            {
                source = IO.File.ReadAllText("plugins/nws-client-es5.js");
                memoryCache.Set(memKey, source);
            }

            if (source.Contains("{localhost}"))
                source = source.Replace("{localhost}", host);

            return Content(source, "application/javascript; charset=utf-8");
        }
        #endregion

        #region signalr-6.0.25_es5.js
        [HttpGet]
        [AllowAnonymous]
        [Route("signalr-6.0.25_es5.js")]
        public ActionResult SignalrJs()
        {
            string memKey = "ApiController:signalr-6.0.25_es5.js";
            if (!memoryCache.TryGetValue(memKey, out string source))
            {
                source = IO.File.ReadAllText("plugins/signalr-6.0.25_es5.js");
                memoryCache.Set(memKey, source);
            }

            return Content(source, "application/javascript; charset=utf-8");
        }
        #endregion
    }
}