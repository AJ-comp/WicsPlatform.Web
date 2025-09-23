using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using System.Data;

namespace WicsPlatform.Server.Controllers.wics
{
    [Route("odata/wics/MapScheduleMedia")]
    public partial class MapScheduleMediaController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public MapScheduleMediaController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.MapScheduleMedium> GetMapScheduleMedia()
        {
            var items = this.context.MapScheduleMedia.AsQueryable<WicsPlatform.Server.Models.wics.MapScheduleMedium>();
            this.OnMapScheduleMediaRead(ref items);

            return items;
        }

        partial void OnMapScheduleMediaRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapScheduleMedium> items);

        partial void OnMapScheduleMediumGet(ref SingleResult<WicsPlatform.Server.Models.wics.MapScheduleMedium> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/MapScheduleMedia(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.MapScheduleMedium> GetMapScheduleMedium(ulong key)
        {
            var items = this.context.MapScheduleMedia.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnMapScheduleMediumGet(ref result);

            return result;
        }
        partial void OnMapScheduleMediumDeleted(WicsPlatform.Server.Models.wics.MapScheduleMedium item);
        partial void OnAfterMapScheduleMediumDeleted(WicsPlatform.Server.Models.wics.MapScheduleMedium item);

        [HttpDelete("/odata/wics/MapScheduleMedia(Id={Id})")]
        public IActionResult DeleteMapScheduleMedium(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.MapScheduleMedia
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnMapScheduleMediumDeleted(item);
                this.context.MapScheduleMedia.Remove(item);
                this.context.SaveChanges();
                this.OnAfterMapScheduleMediumDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapScheduleMediumUpdated(WicsPlatform.Server.Models.wics.MapScheduleMedium item);
        partial void OnAfterMapScheduleMediumUpdated(WicsPlatform.Server.Models.wics.MapScheduleMedium item);

        [HttpPut("/odata/wics/MapScheduleMedia(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutMapScheduleMedium(ulong key, [FromBody]WicsPlatform.Server.Models.wics.MapScheduleMedium item)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                if (item == null || (item.Id != key))
                {
                    return BadRequest();
                }
                this.OnMapScheduleMediumUpdated(item);
                this.context.MapScheduleMedia.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapScheduleMedia.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Medium,Schedule");
                this.OnAfterMapScheduleMediumUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/MapScheduleMedia(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchMapScheduleMedium(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.MapScheduleMedium> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.MapScheduleMedia.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnMapScheduleMediumUpdated(item);
                this.context.MapScheduleMedia.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapScheduleMedia.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Medium,Schedule");
                this.OnAfterMapScheduleMediumUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapScheduleMediumCreated(WicsPlatform.Server.Models.wics.MapScheduleMedium item);
        partial void OnAfterMapScheduleMediumCreated(WicsPlatform.Server.Models.wics.MapScheduleMedium item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.MapScheduleMedium item)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                if (item == null)
                {
                    return BadRequest();
                }

                this.OnMapScheduleMediumCreated(item);
                this.context.MapScheduleMedia.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapScheduleMedia.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Medium,Schedule");

                this.OnAfterMapScheduleMediumCreated(item);

                return new ObjectResult(SingleResult.Create(itemToReturn))
                {
                    StatusCode = 201
                };
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }
    }
}
