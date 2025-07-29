using System;
using System.Net;
using System.Data;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace WicsPlatform.Server.Controllers.wics
{
    [Route("odata/wics/MapChannelTts")]
    public partial class MapChannelTtsController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public MapChannelTtsController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.MapChannelTt> GetMapChannelTts()
        {
            var items = this.context.MapChannelTts.AsQueryable<WicsPlatform.Server.Models.wics.MapChannelTt>();
            this.OnMapChannelTtsRead(ref items);

            return items;
        }

        partial void OnMapChannelTtsRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapChannelTt> items);

        partial void OnMapChannelTtGet(ref SingleResult<WicsPlatform.Server.Models.wics.MapChannelTt> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/MapChannelTts(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.MapChannelTt> GetMapChannelTt(ulong key)
        {
            var items = this.context.MapChannelTts.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnMapChannelTtGet(ref result);

            return result;
        }
        partial void OnMapChannelTtDeleted(WicsPlatform.Server.Models.wics.MapChannelTt item);
        partial void OnAfterMapChannelTtDeleted(WicsPlatform.Server.Models.wics.MapChannelTt item);

        [HttpDelete("/odata/wics/MapChannelTts(Id={Id})")]
        public IActionResult DeleteMapChannelTt(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.MapChannelTts
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnMapChannelTtDeleted(item);
                this.context.MapChannelTts.Remove(item);
                this.context.SaveChanges();
                this.OnAfterMapChannelTtDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapChannelTtUpdated(WicsPlatform.Server.Models.wics.MapChannelTt item);
        partial void OnAfterMapChannelTtUpdated(WicsPlatform.Server.Models.wics.MapChannelTt item);

        [HttpPut("/odata/wics/MapChannelTts(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutMapChannelTt(ulong key, [FromBody]WicsPlatform.Server.Models.wics.MapChannelTt item)
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
                this.OnMapChannelTtUpdated(item);
                this.context.MapChannelTts.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelTts.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Tt");
                this.OnAfterMapChannelTtUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/MapChannelTts(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchMapChannelTt(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.MapChannelTt> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.MapChannelTts.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnMapChannelTtUpdated(item);
                this.context.MapChannelTts.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelTts.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Tt");
                this.OnAfterMapChannelTtUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapChannelTtCreated(WicsPlatform.Server.Models.wics.MapChannelTt item);
        partial void OnAfterMapChannelTtCreated(WicsPlatform.Server.Models.wics.MapChannelTt item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.MapChannelTt item)
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

                this.OnMapChannelTtCreated(item);
                this.context.MapChannelTts.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelTts.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Tt");

                this.OnAfterMapChannelTtCreated(item);

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
