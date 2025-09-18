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
    [Route("odata/wics/MapSpeakerGroups")]
    public partial class MapSpeakerGroupsController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public MapSpeakerGroupsController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.MapSpeakerGroup> GetMapSpeakerGroups()
        {
            var items = this.context.MapSpeakerGroups.AsQueryable<WicsPlatform.Server.Models.wics.MapSpeakerGroup>();
            this.OnMapSpeakerGroupsRead(ref items);

            return items;
        }

        partial void OnMapSpeakerGroupsRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapSpeakerGroup> items);

        partial void OnMapSpeakerGroupGet(ref SingleResult<WicsPlatform.Server.Models.wics.MapSpeakerGroup> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/MapSpeakerGroups(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.MapSpeakerGroup> GetMapSpeakerGroup(ulong key)
        {
            var items = this.context.MapSpeakerGroups.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnMapSpeakerGroupGet(ref result);

            return result;
        }
        partial void OnMapSpeakerGroupDeleted(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);
        partial void OnAfterMapSpeakerGroupDeleted(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);

        [HttpDelete("/odata/wics/MapSpeakerGroups(Id={Id})")]
        public IActionResult DeleteMapSpeakerGroup(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.MapSpeakerGroups
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnMapSpeakerGroupDeleted(item);
                this.context.MapSpeakerGroups.Remove(item);
                this.context.SaveChanges();
                this.OnAfterMapSpeakerGroupDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapSpeakerGroupUpdated(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);
        partial void OnAfterMapSpeakerGroupUpdated(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);

        [HttpPut("/odata/wics/MapSpeakerGroups(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutMapSpeakerGroup(ulong key, [FromBody]WicsPlatform.Server.Models.wics.MapSpeakerGroup item)
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
                this.OnMapSpeakerGroupUpdated(item);
                this.context.MapSpeakerGroups.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapSpeakerGroups.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Group,Speaker");
                this.OnAfterMapSpeakerGroupUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/MapSpeakerGroups(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchMapSpeakerGroup(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.MapSpeakerGroup> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.MapSpeakerGroups.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnMapSpeakerGroupUpdated(item);
                this.context.MapSpeakerGroups.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapSpeakerGroups.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Group,Speaker");
                this.OnAfterMapSpeakerGroupUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapSpeakerGroupCreated(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);
        partial void OnAfterMapSpeakerGroupCreated(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.MapSpeakerGroup item)
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

                this.OnMapSpeakerGroupCreated(item);
                this.context.MapSpeakerGroups.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapSpeakerGroups.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Group,Speaker");

                this.OnAfterMapSpeakerGroupCreated(item);

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
