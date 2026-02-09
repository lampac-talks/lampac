using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Engine.Utilities;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using IO = System.IO;

namespace Lampac.Controllers
{
    public class StorageController : BaseController
    {
        #region backup.js
        [HttpGet]
        [AllowAnonymous]
        [Route("backup.js")]
        [Route("backup/js/{token}")]
        public ActionResult Backup(string token)
        {
            if (!AppInit.conf.storage.enable)
                return Content(string.Empty, "application/javascript; charset=utf-8");

            var sb = new StringBuilder(FileCache.ReadAllText("plugins/backup.js"));

            sb.Replace("{localhost}", host)
              .Replace("{token}", HttpUtility.UrlEncode(token));

            return Content(sb.ToString(), "application/javascript; charset=utf-8");
        }
        #endregion

        #region sync.js
        [HttpGet]
        [AllowAnonymous]
        [Route("sync.js")]
        [Route("sync/js/{token}")]
        public ActionResult SyncJS(string token, bool lite)
        {
            if (!AppInit.conf.storage.enable)
                return Content(string.Empty, "application/javascript; charset=utf-8");

            StringBuilder sb;

            if (lite || AppInit.conf.sync_user.version == 1)
            {
                sb = new StringBuilder(FileCache.ReadAllText($"plugins/{(lite ? "sync_lite" : "sync")}.js"));
            }
            else
            {
                sb = new StringBuilder(FileCache.ReadAllText("plugins/sync_v2/sync.js"));
            }

            sb.Replace("{sync-invc}", FileCache.ReadAllText("plugins/sync-invc.js"))
              .Replace("{localhost}", host)
              .Replace("{token}", HttpUtility.UrlEncode(token));

            return Content(sb.ToString(), "application/javascript; charset=utf-8");
        }
        #endregion


        #region Get
        [HttpGet]
        [Route("/storage/get")]
        async public Task<ActionResult> Get(string path, string pathfile, bool responseInfo)
        {
            if (!AppInit.conf.storage.enable)
                return ContentTo("{\"success\": false, \"msg\": \"disabled\"}");

            string outFile = StorageManager.GetFilePath(path, false, requestInfo, pathfile);
            if (outFile == null || !IO.File.Exists(outFile))
                return ContentTo("{\"success\": false, \"msg\": \"outFile\"}");

            var file = new FileInfo(outFile);
            var fileInfo = new { file.Name, path = outFile, file.Length, changeTime = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds() };

            if (responseInfo)
                return Json(new { success = true, uid = requestInfo.user_uid, fileInfo });

            string data;

            if (AppInit.conf.storage.brotli)
            {
                data = await BrotliTo.DecompressAsync(outFile);
            }
            else
            {
                var semaphore = new SemaphorManager(outFile, TimeSpan.FromSeconds(20));

                try
                {
                    await semaphore.WaitAsync();

                    data = await IO.File.ReadAllTextAsync(outFile);
                }
                catch
                {
                    HttpContext.Response.StatusCode = 503;
                    return ContentTo("{\"success\": false, \"msg\": \"fileLock\"}");
                }
                finally
                {
                    semaphore.Release();
                }
            }

            return Json(new { success = true, uid = requestInfo.user_uid, fileInfo, data });
        }
        #endregion

        #region Set
        [HttpPost]
        [Route("/storage/set")]
        async public Task<ActionResult> Set([FromQuery]string path, [FromQuery]string pathfile, [FromQuery]string connectionId, [FromQuery]string events)
        {
            if (!AppInit.conf.storage.enable)
                return ContentTo("{\"success\": false, \"msg\": \"disabled\"}");

            if (HttpContext.Request.ContentLength > AppInit.conf.storage.max_size)
                return ContentTo("{\"success\": false, \"msg\": \"max_size\"}");

            string outFile = StorageManager.GetFilePath(path, true, requestInfo, pathfile);
            if (outFile == null)
                return ContentTo("{\"success\": false, \"msg\": \"outFile\"}");

            using (var memoryStream = PoolInvk.msm.GetStream())
            {
                try
                {
                    await HttpContext.Request.Body.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;
                }
                catch
                {
                    HttpContext.Response.StatusCode = 503;
                    return ContentTo("{\"success\": false, \"msg\": \"Request.Body.CopyToAsync\"}");
                }

                if (AppInit.conf.storage.brotli)
                {
                    await BrotliTo.CompressAsync(outFile, memoryStream);
                }
                else
                {
                    var semaphore = new SemaphorManager(outFile, TimeSpan.FromSeconds(20));

                    try
                    {
                        await semaphore.WaitAsync();

                        using (var fileStream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, PoolInvk.bufferSize))
                        {
                            await memoryStream.CopyToAsync(fileStream, PoolInvk.bufferSize);
                        }
                    }
                    catch
                    {
                        HttpContext.Response.StatusCode = 503;
                        return ContentTo("{\"success\": false, \"msg\": \"fileLock\"}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
            }

            #region events
            if (!string.IsNullOrEmpty(events))
            {
                try
                {
                    var json = JsonConvert.DeserializeObject<JObject>(CrypTo.DecodeBase64(events));
                    _ = Shared.Startup.WS.EventsAsync(json.Value<string>("connectionId"), requestInfo.user_uid, json.Value<string>("name"), json.Value<string>("data")).ConfigureAwait(false);
                    _ = Shared.Startup.Nws.EventsAsync(json.Value<string>("connectionId"), requestInfo.user_uid, json.Value<string>("name"), json.Value<string>("data")).ConfigureAwait(false);
                }
                catch { }
            }
            else
            {
                string edata = JsonConvertPool.SerializeObject(new { path, pathfile });
                _ = Shared.Startup.Nws.EventsAsync(connectionId, requestInfo.user_uid, "storage", edata).ConfigureAwait(false);
            }
            #endregion

            var inf = new FileInfo(outFile);

            return Json(new 
            { 
                success = true,
                uid = requestInfo.user_uid,
                fileInfo = new { inf.Name, path = outFile, inf.Length, changeTime = new DateTimeOffset(inf.LastWriteTimeUtc).ToUnixTimeMilliseconds() }
            });
        }
        #endregion

        #region TempGet
        [HttpGet]
        [Route("/storage/temp/{key}")]
        async public Task<ActionResult> TempGet(string key, bool responseInfo)
        {
            if (!AppInit.conf.storage.enable)
                return ContentTo("{\"success\": false, \"msg\": \"disabled\"}");

            string outFile = StorageManager.GetFilePath("temp", false, key);
            if (outFile == null || !IO.File.Exists(outFile))
                return ContentTo("{\"success\": false, \"msg\": \"outFile\"}");

            var file = new FileInfo(outFile);
            var fileInfo = new { file.Name, path = outFile, file.Length, changeTime = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds() };

            if (responseInfo)
                return Json(new { success = true, uid = requestInfo.user_uid, fileInfo });

            string data;

            if (AppInit.conf.storage.brotli)
            {
                data = await BrotliTo.DecompressAsync(outFile);
            }
            else
            {
                var semaphore = new SemaphorManager(outFile, TimeSpan.FromSeconds(20));

                try
                {
                    await semaphore.WaitAsync();

                    data = await IO.File.ReadAllTextAsync(outFile);
                }
                catch
                {
                    HttpContext.Response.StatusCode = 503;
                    return ContentTo("{\"success\": false, \"msg\": \"fileLock\"}");
                }
                finally
                {
                    semaphore.Release();
                }
            }

            return Json(new { success = true, uid = requestInfo.user_uid, fileInfo, data });
        }
        #endregion

        #region TempSet
        [HttpPost]
        [Route("/storage/temp/{key}")]
        async public Task<ActionResult> TempSet(string key)
        {
            if (!AppInit.conf.storage.enable)
                return ContentTo("{\"success\": false, \"msg\": \"disabled\"}");

            if (HttpContext.Request.ContentLength > AppInit.conf.storage.max_size)
                return ContentTo("{\"success\": false, \"msg\": \"max_size\"}");

            string outFile = StorageManager.GetFilePath("temp", true, key);
            if (outFile == null)
                return ContentTo("{\"success\": false, \"msg\": \"outFile\"}");

            using (var memoryStream = PoolInvk.msm.GetStream())
            {
                try
                {
                    await HttpContext.Request.Body.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;
                }
                catch
                {
                    HttpContext.Response.StatusCode = 503;
                    return ContentTo("{\"success\": false, \"msg\": \"Request.Body.CopyToAsync\"}");
                }

                if (AppInit.conf.storage.brotli)
                {
                    await BrotliTo.CompressAsync(outFile, memoryStream);
                }
                else
                {
                    var semaphore = new SemaphorManager(outFile, TimeSpan.FromSeconds(20));

                    try
                    {
                        await semaphore.WaitAsync();

                        using (var fileStream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, PoolInvk.bufferSize))
                            await memoryStream.CopyToAsync(fileStream, PoolInvk.bufferSize);
                    }
                    catch
                    {
                        HttpContext.Response.StatusCode = 503;
                        return ContentTo("{\"success\": false, \"msg\": \"fileLock\"}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
            }

            var inf = new FileInfo(outFile);

            return Json(new
            {
                success = true,
                uid = requestInfo.user_uid,
                fileInfo = new { inf.Name, path = outFile, inf.Length, changeTime = new DateTimeOffset(inf.LastWriteTimeUtc).ToUnixTimeMilliseconds() }
            });
        }
        #endregion
    }
}