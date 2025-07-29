using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Server.Controllers
{
    [ApiController]
    // ��Ʈ�ѷ� ���Ʈ: /upload/...
    // (�̹� �ٸ� ������ "upload/..."�� ���� ������ �浹 ����)
    public partial class UploadController : Controller
    {
        private readonly IWebHostEnvironment environment;
        private readonly wicsContext context;

        public UploadController(IWebHostEnvironment environment, WicsPlatform.Server.Data.wicsContext context)
        {
            this.environment = environment;
            this.context = context;
        }

        /// <summary>
        /// ���� ���� ���ε�
        /// POST /upload/single
        /// form-data: file
        /// </summary>
        [HttpPost("upload/single")]
        public IActionResult Single(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest("No file selected or file is empty.");
                }

                // ���ε� ���� ���� (wwwroot/Uploads)
                var uploadFolder = Path.Combine(environment.WebRootPath, "Uploads");
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                }

                // ������ ���� ���ϸ� (GUID + Ȯ����)
                var ext = Path.GetExtension(file.FileName);
                var newFileName = $"{Guid.NewGuid()}{ext}";
                var filePath = Path.Combine(uploadFolder, newFileName);

                // ���� ����
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                // ��ȯ�� URL (�ʿ��ϸ�)
                var fileUrl = $"{Request.Scheme}://{Request.Host}/Uploads/{newFileName}";

                return Ok(new
                {
                    success = true,
                    message = "File uploaded successfully.",
                    fileUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        /// <summary>
        /// ���� ���� ���ε�
        /// POST /upload/multiple
        /// form-data: files[]
        /// </summary>
        [HttpPost("upload/multiple")]
        public IActionResult Multiple(IFormFile[] files)
        {
            try
            {
                if (files == null || files.Length == 0)
                {
                    return BadRequest("No files selected.");
                }

                var uploadFolder = Path.Combine(environment.WebRootPath, "Uploads");
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                }

                // ���ε� ����� ���� ����Ʈ
                var fileUrls = files.Select(file =>
                {
                    var ext = Path.GetExtension(file.FileName);
                    var newFileName = $"{Guid.NewGuid()}{ext}";
                    var filePath = Path.Combine(uploadFolder, newFileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        file.CopyTo(stream);
                    }

                    // ��ȯ URL
                    var url = $"{Request.Scheme}://{Request.Host}/Uploads/{newFileName}";
                    return url;
                }).ToList();

                return Ok(new
                {
                    success = true,
                    message = $"{files.Length} files uploaded successfully.",
                    files = fileUrls
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        /// <summary>
        /// ���� ���� ���ε� + ��� �Ķ����
        /// POST /upload/{id}
        /// form-data: files[]
        /// </summary>
        [HttpPost("upload/{id}")]
        public IActionResult Post(IFormFile[] files, int id)
        {
            try
            {
                if (files == null || files.Length == 0)
                {
                    return BadRequest("No files selected.");
                }

                var uploadFolder = Path.Combine(environment.WebRootPath, "Uploads");
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                }

                var fileUrls = files.Select(file =>
                {
                    var ext = Path.GetExtension(file.FileName);
                    var newFileName = $"{Guid.NewGuid()}{ext}";
                    var filePath = Path.Combine(uploadFolder, newFileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        file.CopyTo(stream);
                    }

                    var url = $"{Request.Scheme}://{Request.Host}/Uploads/{newFileName}";
                    return url;
                }).ToList();

                // 'id'�� �̿��� ���� �۾��� �Ѵٸ� ���� �߰�
                // ex) DB�� file ��� ����?

                return Ok(new
                {
                    success = true,
                    message = $"{files.Length} files uploaded successfully. Param id: {id}",
                    files = fileUrls
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        /// <summary>
        /// �̹��� ���ε� (HtmlEditor ��� ���)
        /// POST /upload/image
        /// form-data: file
        /// </summary>
        [HttpPost("upload/image")]
        public IActionResult Image(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest("No image file selected.");
                }

                // �̹������� Ȯ���� �˻� �� �߰� ����
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

                var uploadFolder = Path.Combine(environment.WebRootPath, "Uploads");
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                }

                var filePath = Path.Combine(uploadFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                // �̹��� ������ URL
                var url = $"{Request.Scheme}://{Request.Host}/Uploads/{fileName}";

                return Ok(new
                {
                    success = true,
                    message = "Image uploaded successfully.",
                    url
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }



        [HttpPost("upload/media")]
        public IActionResult Media(IFormFile file, [FromForm] ulong? groupId)
        {
            try
            {
                // 1) ������ null�̰ų� ũ�Ⱑ 0���� üũ
                if (file == null || file.Length == 0)
                {
                    return BadRequest("No file uploaded.");
                }

                // (����) ���ε� ����: wwwroot/Uploads
                var uploadFolder = Path.Combine(environment.WebRootPath, "Uploads");
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                }

                // �� ���ϸ�: GUID + Ȯ����
                var ext = Path.GetExtension(file.FileName);
                var newFileName = $"{Guid.NewGuid()}{ext}";
                var filePath = Path.Combine(uploadFolder, newFileName);

                // ���� ���� ����
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                // ���ε� �� URL(���ϸ� Ŭ���̾�Ʈ���� ��ȯ)
                var fileUrl = $"{Request.Scheme}://{Request.Host}/Uploads/{newFileName}";

                // 2) ���ε� ��ó��: Media ���̺�� ���ڵ� �߰�
                //  - ���⼭ DB ���ؽ�Ʈ�� ���� ���Թްų�,
                //    MediaController�� ȣ��(����õ)�� ���� ������,
                //    ������ DB ���ؽ�Ʈ�� �� ��Ʈ�ѷ����� Inject�ؼ� ���� �����մϴ�.

                // ��: DB ���� (pseudo code)
                var medium = new Medium
                {
                    FullPath = $"/Uploads/{newFileName}",
                    FileName = file.FileName,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };
                context.Media.Add(medium);
                context.SaveChanges();

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
                }

                // ���� ��ȯ
                return Ok(new
                {
                    success = true,
                    message = "File uploaded and media record created successfully.",
                    url = fileUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

    }
}
