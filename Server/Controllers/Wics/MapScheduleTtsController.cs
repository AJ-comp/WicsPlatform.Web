using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using System.Data;

namespace WicsPlatform.Server.Controllers.wics
{
    [Route("odata/wics/MapScheduleTts")]
    public partial class MapScheduleTtsController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public MapScheduleTtsController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.MapScheduleTt> GetMapScheduleTts()
        {
            var items = this.context.MapScheduleTts.AsQueryable<WicsPlatform.Server.Models.wics.MapScheduleTt>();
            this.OnMapScheduleTtsRead(ref items);

            return items;
        }

        partial void OnMapScheduleTtsRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapScheduleTt> items);

        partial void OnMapScheduleTtGet(ref SingleResult<WicsPlatform.Server.Models.wics.MapScheduleTt> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/MapScheduleTts(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.MapScheduleTt> GetMapScheduleTt(ulong key)
        {
            var items = this.context.MapScheduleTts.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnMapScheduleTtGet(ref result);

            return result;
        }
        partial void OnMapScheduleTtDeleted(WicsPlatform.Server.Models.wics.MapScheduleTt item);
        partial void OnAfterMapScheduleTtDeleted(WicsPlatform.Server.Models.wics.MapScheduleTt item);

        [HttpDelete("/odata/wics/MapScheduleTts(Id={Id})")]
        public IActionResult DeleteMapScheduleTt(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.MapScheduleTts
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnMapScheduleTtDeleted(item);
                this.context.MapScheduleTts.Remove(item);
                this.context.SaveChanges();
                this.OnAfterMapScheduleTtDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapScheduleTtUpdated(WicsPlatform.Server.Models.wics.MapScheduleTt item);
        partial void OnAfterMapScheduleTtUpdated(WicsPlatform.Server.Models.wics.MapScheduleTt item);

        [HttpPut("/odata/wics/MapScheduleTts(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutMapScheduleTt(ulong key, [FromBody]WicsPlatform.Server.Models.wics.MapScheduleTt item)
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
                this.OnMapScheduleTtUpdated(item);
                this.context.MapScheduleTts.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapScheduleTts.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Schedule,Tt");
                this.OnAfterMapScheduleTtUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/MapScheduleTts(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchMapScheduleTt(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.MapScheduleTt> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.MapScheduleTts.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnMapScheduleTtUpdated(item);
                this.context.MapScheduleTts.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapScheduleTts.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Schedule,Tt");
                this.OnAfterMapScheduleTtUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapScheduleTtCreated(WicsPlatform.Server.Models.wics.MapScheduleTt item);
        partial void OnAfterMapScheduleTtCreated(WicsPlatform.Server.Models.wics.MapScheduleTt item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.MapScheduleTt item)
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

                this.OnMapScheduleTtCreated(item);
                this.context.MapScheduleTts.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapScheduleTts.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Schedule,Tt");

                this.OnAfterMapScheduleTtCreated(item);

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
