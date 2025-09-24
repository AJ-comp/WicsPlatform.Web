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
    [Route("odata/wics/MapChannelGroups")]
    public partial class MapChannelGroupsController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public MapChannelGroupsController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.MapChannelGroup> GetMapChannelGroups()
        {
            var items = this.context.MapChannelGroups.AsQueryable<WicsPlatform.Server.Models.wics.MapChannelGroup>();
            this.OnMapChannelGroupsRead(ref items);

            return items;
        }

        partial void OnMapChannelGroupsRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapChannelGroup> items);

        partial void OnMapChannelGroupGet(ref SingleResult<WicsPlatform.Server.Models.wics.MapChannelGroup> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/MapChannelGroups(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.MapChannelGroup> GetMapChannelGroup(ulong key)
        {
            var items = this.context.MapChannelGroups.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnMapChannelGroupGet(ref result);

            return result;
        }
        partial void OnMapChannelGroupDeleted(WicsPlatform.Server.Models.wics.MapChannelGroup item);
        partial void OnAfterMapChannelGroupDeleted(WicsPlatform.Server.Models.wics.MapChannelGroup item);

        [HttpDelete("/odata/wics/MapChannelGroups(Id={Id})")]
        public IActionResult DeleteMapChannelGroup(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.MapChannelGroups
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnMapChannelGroupDeleted(item);
                this.context.MapChannelGroups.Remove(item);
                this.context.SaveChanges();
                this.OnAfterMapChannelGroupDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapChannelGroupUpdated(WicsPlatform.Server.Models.wics.MapChannelGroup item);
        partial void OnAfterMapChannelGroupUpdated(WicsPlatform.Server.Models.wics.MapChannelGroup item);

        [HttpPut("/odata/wics/MapChannelGroups(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutMapChannelGroup(ulong key, [FromBody]WicsPlatform.Server.Models.wics.MapChannelGroup item)
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
                this.OnMapChannelGroupUpdated(item);
                this.context.MapChannelGroups.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelGroups.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Group");
                this.OnAfterMapChannelGroupUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/MapChannelGroups(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchMapChannelGroup(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.MapChannelGroup> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.MapChannelGroups.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnMapChannelGroupUpdated(item);
                this.context.MapChannelGroups.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelGroups.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Group");
                this.OnAfterMapChannelGroupUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapChannelGroupCreated(WicsPlatform.Server.Models.wics.MapChannelGroup item);
        partial void OnAfterMapChannelGroupCreated(WicsPlatform.Server.Models.wics.MapChannelGroup item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.MapChannelGroup item)
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

                this.OnMapChannelGroupCreated(item);
                this.context.MapChannelGroups.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelGroups.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Group");

                this.OnAfterMapChannelGroupCreated(item);

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
