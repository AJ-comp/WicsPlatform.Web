using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Models.wics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Principal;

namespace WicsPlatform.Server.Controllers
{
    [ApiController]
    [Route("upload")]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(200_000_000)] // 200 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
    public partial class UploadController : Controller
    {
        private readonly IWebHostEnvironment environment;
        private readonly wicsContext context;
        private readonly ILogger<UploadController> logger;
        private readonly IConfiguration config;

        public UploadController(IWebHostEnvironment environment, WicsPlatform.Server.Data.wicsContext context, ILogger<UploadController> logger, IConfiguration config)
        {
            this.environment = environment;
            this.context = context;
            this.logger = logger;
            this.config = config;
        }

        private void LogDiag(string message)
        {
            try
            {
                logger.LogInformation(message);
                var enabled = config.GetValue<bool>("UploadLogging:Enabled");
                if (!enabled) return;
                var baseDir = config["UploadLogging:LogPath"];
                if (string.IsNullOrWhiteSpace(baseDir))
                {
                    baseDir = Path.Combine(environment.ContentRootPath, "logs");
                }
                else if (!Path.IsPathRooted(baseDir))
                {
                    baseDir = Path.Combine(environment.ContentRootPath, baseDir);
                }
                Directory.CreateDirectory(baseDir);
                var file = Path.Combine(baseDir, $"upload-diagnostics-{DateTime.Now:yyyyMMdd}.log");
                System.IO.File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch
            {
                // ignore logging failures
            }
        }

        private void LogEnvInfo(string uploadFolder)
        {
            try
            {
                var webRoot = environment.WebRootPath;
                var contentRoot = environment.ContentRootPath;
                var user = Environment.UserName;
                var winIdentity = string.Empty;
                try { winIdentity = WindowsIdentity.GetCurrent()?.Name ?? string.Empty; } catch { }
                LogDiag($"ENV: WebRootPath={webRoot} ContentRootPath={contentRoot} UploadFolder={uploadFolder} User={user} WinIdentity={winIdentity}");
            }
            catch { }
        }

        private void ProbeWriteAccess(string folder)
        {
            try
            {
                var probe = Path.Combine(folder, $"__write_probe_{Guid.NewGuid():N}.tmp");
                System.IO.File.WriteAllText(probe, "probe");
                System.IO.File.Delete(probe);
                LogDiag($"WRITE PROBE: success at {folder}");
            }
            catch (Exception ex)
            {
                LogDiag($"WRITE PROBE: failed at {folder} -> {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 단일 파일 업로드
        /// POST /upload/single
        /// form-data: file
        /// </summary>
        [HttpPost("single")]
        [Consumes("multipart/form-data")]
        public IActionResult Single(IFormFile file)
        {
            try
            {
                LogDiag($"BEGIN Single upload: name={file?.FileName}, length={file?.Length}");
                if (file == null || file.Length == 0)
                {
                    LogDiag("ABORT Single: no file or empty");
                    return BadRequest("No file selected or file is empty.");
                }

                var uploadFolder = Path.Combine(environment.WebRootPath, "Uploads");
                LogEnvInfo(uploadFolder);
                var exists = Directory.Exists(uploadFolder);
                LogDiag($"FOLDER EXISTS? {exists}");
                if (!exists)
                {
                    try
                    {
                        Directory.CreateDirectory(uploadFolder);
                        LogDiag("FOLDER CREATE: success");
                    }
                    catch (Exception dex)
                    {
                        LogDiag($"FOLDER CREATE: failed -> {dex.GetType().Name}: {dex.Message}");
                        throw;
                    }
                }

                ProbeWriteAccess(uploadFolder);

                var ext = Path.GetExtension(file.FileName);
                var newFileName = $"{Guid.NewGuid()}{ext}";
                var filePath = Path.Combine(uploadFolder, newFileName);

                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        file.CopyTo(stream);
                    }
                    LogDiag($"SAVE: success -> {filePath} ({file.Length} bytes)");
                }
                catch (UnauthorizedAccessException uae)
                {
                    LogDiag($"SAVE: unauthorized -> {uae.Message}");
                    return StatusCode(StatusCodes.Status500InternalServerError, $"Access denied to path {filePath}");
                }
                catch (Exception ex)
                {
                    LogDiag($"SAVE: failed -> {ex.GetType().Name}: {ex.Message}");
                    throw;
                }

                var fileUrl = $"{Request.Scheme}://{Request.Host}/Uploads/{newFileName}";
                LogDiag($"END Single: url={fileUrl}");

                return Ok(new
                {
                    success = true,
                    message = "File uploaded successfully.",
                    fileUrl
                });
            }
            catch (Exception ex)
            {
                LogDiag($"ERROR Single: {ex.GetType().Name}: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        /// <summary>
        /// 다중 파일 업로드
        /// POST /upload/multiple
        /// form-data: files[]
        /// </summary>
        [HttpPost("multiple")]
        [Consumes("multipart/form-data")]
        public IActionResult Multiple(IFormFile[] files)
        {
            try
            {
                LogDiag($"BEGIN Multiple upload: count={files?.Length}");
                if (files == null || files.Length == 0)
                {
                    LogDiag("ABORT Multiple: no files");
                    return BadRequest("No files selected.");
                }

                var uploadFolder = Path.Combine(environment.WebRootPath, "Uploads");
                LogEnvInfo(uploadFolder);
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                    LogDiag("FOLDER CREATE: success");
                }

                ProbeWriteAccess(uploadFolder);

                var fileUrls = files.Select(f =>
                {
                    var ext = Path.GetExtension(f.FileName);
                    var newFileName = $"{Guid.NewGuid()}{ext}";
                    var filePath = Path.Combine(uploadFolder, newFileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        f.CopyTo(stream);
                    }
                    LogDiag($"SAVE: success -> {filePath} ({f.Length} bytes)");

                    return $"{Request.Scheme}://{Request.Host}/Uploads/{newFileName}";
                }).ToList();

                LogDiag($"END Multiple: ok");
                return Ok(new
                {
                    success = true,
                    message = $"{files.Length} files uploaded successfully.",
                    files = fileUrls
                });
            }
            catch (Exception ex)
            {
                LogDiag($"ERROR Multiple: {ex.GetType().Name}: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        /// <summary>
        /// 다중 파일 업로드 + 경로 파라미터
        /// POST /upload/{id}
        /// form-data: files[]
        /// </summary>
        [HttpPost("{id}")]
        [Consumes("multipart/form-data")]
        public IActionResult Post(IFormFile[] files, int id)
        {
            try
            {
                LogDiag($"BEGIN Post({id}) upload: count={files?.Length}");
                if (files == null || files.Length == 0)
                {
                    LogDiag("ABORT Post: no files");
                    return BadRequest("No files selected.");
                }

                var uploadFolder = Path.Combine(environment.WebRootPath, "Uploads");
                LogEnvInfo(uploadFolder);
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                    LogDiag("FOLDER CREATE: success");
                }

                ProbeWriteAccess(uploadFolder);

                var fileUrls = files.Select(file =>
                {
                    var ext = Path.GetExtension(file.FileName);
                    var newFileName = $"{Guid.NewGuid()}{ext}";
                    var filePath = Path.Combine(uploadFolder, newFileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        file.CopyTo(stream);
                    }
                    LogDiag($"SAVE: success -> {filePath} ({file.Length} bytes)");

                    var url = $"{Request.Scheme}://{Request.Host}/Uploads/{newFileName}";
                    return url;
                }).ToList();

                LogDiag("END Post: ok");
                return Ok(new
                {
                    success = true,
                    message = $"{files.Length} files uploaded successfully. Param id: {id}",
                    files = fileUrls
                });
            }
            catch (Exception ex)
            {
                LogDiag($"ERROR Post: {ex.GetType().Name}: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        /// <summary>
        /// 이미지 업로드 (HtmlEditor 등에서 사용)
        /// POST /upload/image
        /// form-data: file
        /// </summary>
        [HttpPost("image")]
        [Consumes("multipart/form-data")]
        public IActionResult Image(IFormFile file)
        {
            try
            {
                LogDiag($"BEGIN Image upload: name={file?.FileName}, length={file?.Length}");
                if (file == null || file.Length == 0)
                {
                    LogDiag("ABORT Image: no file or empty");
                    return BadRequest("No image file selected.");
                }

                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

                var uploadFolder = Path.Combine(environment.WebRootPath, "Uploads");
                LogEnvInfo(uploadFolder);
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                    LogDiag("FOLDER CREATE: success");
                }

                ProbeWriteAccess(uploadFolder);

                var filePath = Path.Combine(uploadFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }
                LogDiag($"SAVE: success -> {filePath} ({file.Length} bytes)");

                var url = $"{Request.Scheme}://{Request.Host}/Uploads/{fileName}";
                LogDiag($"END Image: url={url}");

                return Ok(new
                {
                    success = true,
                    message = "Image uploaded successfully.",
                    url
                });
            }
            catch (Exception ex)
            {
                LogDiag($"ERROR Image: {ex.GetType().Name}: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        [HttpPost("media")]
        [Consumes("multipart/form-data")]
        public IActionResult Media(IFormFile file, [FromForm] ulong? groupId)
        {
            try
            {
                LogDiag($"BEGIN Media upload: name={file?.FileName}, length={file?.Length}, groupId={groupId}");
                if (file == null || file.Length == 0)
                {
                    LogDiag("ABORT Media: no file or empty");
                    return BadRequest("No file uploaded.");
                }

                var uploadFolder = Path.Combine(environment.WebRootPath, "Uploads");
                LogEnvInfo(uploadFolder);
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                    LogDiag("FOLDER CREATE: success");
                }

                ProbeWriteAccess(uploadFolder);

                var ext = Path.GetExtension(file.FileName);
                var newFileName = $"{Guid.NewGuid()}{ext}";
                var filePath = Path.Combine(uploadFolder, newFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }
                LogDiag($"SAVE: success -> {filePath} ({file.Length} bytes)");

                var fileUrl = $"{Request.Scheme}://{Request.Host}/Uploads/{newFileName}";

                var medium = new Medium
                {
                    FullPath = $"/Uploads/{newFileName}",
                    FileName = file.FileName,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };
                context.Media.Add(medium);
                context.SaveChanges();
                LogDiag($"DB: Medium created -> Id={medium.Id}");

                if (groupId.HasValue)
                {
                    var map = new MapMediaGroup
                    {
                        MediaId = medium.Id,
                        GroupId = groupId.Value,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                    };
                    context.MapMediaGroups.Add(map);
                    context.SaveChanges();
                    LogDiag($"DB: MapMediaGroup created -> Id={map.Id}");
                }

                LogDiag($"END Media: url={fileUrl}");
                return Ok(new
                {
                    success = true,
                    message = "File uploaded and media record created successfully.",
                    url = fileUrl
                });
            }
            catch (Exception ex)
            {
                LogDiag($"ERROR Media: {ex.GetType().Name}: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

    }
}
