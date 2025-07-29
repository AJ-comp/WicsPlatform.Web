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
    [Route("odata/wics/MapMediaGroups")]
    public partial class MapMediaGroupsController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public MapMediaGroupsController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.MapMediaGroup> GetMapMediaGroups()
        {
            var items = this.context.MapMediaGroups.AsQueryable<WicsPlatform.Server.Models.wics.MapMediaGroup>();
            this.OnMapMediaGroupsRead(ref items);

            return items;
        }

        partial void OnMapMediaGroupsRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapMediaGroup> items);

        partial void OnMapMediaGroupGet(ref SingleResult<WicsPlatform.Server.Models.wics.MapMediaGroup> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/MapMediaGroups(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.MapMediaGroup> GetMapMediaGroup(ulong Id)
        {
            var items = this.context.MapMediaGroups.Where(i => i.Id == Id);
            var result = SingleResult.Create(items);

            OnMapMediaGroupGet(ref result);

            return result;
        }
        partial void OnMapMediaGroupDeleted(WicsPlatform.Server.Models.wics.MapMediaGroup item);
        partial void OnAfterMapMediaGroupDeleted(WicsPlatform.Server.Models.wics.MapMediaGroup item);

        [HttpDelete("/odata/wics/MapMediaGroups(Id={Id})")]
        public IActionResult DeleteMapMediaGroup(ulong Id)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.MapMediaGroups
                    .Where(i => i.Id == Id)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnMapMediaGroupDeleted(item);
                this.context.MapMediaGroups.Remove(item);
                this.context.SaveChanges();
                this.OnAfterMapMediaGroupDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapMediaGroupUpdated(WicsPlatform.Server.Models.wics.MapMediaGroup item);
        partial void OnAfterMapMediaGroupUpdated(WicsPlatform.Server.Models.wics.MapMediaGroup item);

        [HttpPut("/odata/wics/MapMediaGroups(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutMapMediaGroup(ulong Id, [FromBody]WicsPlatform.Server.Models.wics.MapMediaGroup item)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                if (item == null || (item.Id != Id))
                {
                    return BadRequest();
                }
                this.OnMapMediaGroupUpdated(item);
                this.context.MapMediaGroups.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapMediaGroups.Where(i => i.Id == Id);
                Request.QueryString = Request.QueryString.Add("$expand", "Group,Medium");
                this.OnAfterMapMediaGroupUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/MapMediaGroups(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchMapMediaGroup(ulong Id, [FromBody]Delta<WicsPlatform.Server.Models.wics.MapMediaGroup> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.MapMediaGroups.Where(i => i.Id == Id).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnMapMediaGroupUpdated(item);
                this.context.MapMediaGroups.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapMediaGroups.Where(i => i.Id == Id);
                Request.QueryString = Request.QueryString.Add("$expand", "Group,Medium");
                this.OnAfterMapMediaGroupUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapMediaGroupCreated(WicsPlatform.Server.Models.wics.MapMediaGroup item);
        partial void OnAfterMapMediaGroupCreated(WicsPlatform.Server.Models.wics.MapMediaGroup item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.MapMediaGroup item)
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

                this.OnMapMediaGroupCreated(item);
                this.context.MapMediaGroups.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapMediaGroups.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Group,Medium");

                this.OnAfterMapMediaGroupCreated(item);

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
