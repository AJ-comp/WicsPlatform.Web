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
    [Route("odata/wics/Groups")]
    public partial class GroupsController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public GroupsController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.Group> GetGroups()
        {
            var items = this.context.Groups.AsQueryable<WicsPlatform.Server.Models.wics.Group>();
            this.OnGroupsRead(ref items);

            return items;
        }

        partial void OnGroupsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Group> items);

        partial void OnGroupGet(ref SingleResult<WicsPlatform.Server.Models.wics.Group> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/Groups(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.Group> GetGroup(ulong Id)
        {
            var items = this.context.Groups.Where(i => i.Id == Id);
            var result = SingleResult.Create(items);

            OnGroupGet(ref result);

            return result;
        }
        partial void OnGroupDeleted(WicsPlatform.Server.Models.wics.Group item);
        partial void OnAfterGroupDeleted(WicsPlatform.Server.Models.wics.Group item);

        [HttpDelete("/odata/wics/Groups(Id={Id})")]
        public IActionResult DeleteGroup(ulong Id)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.Groups
                    .Where(i => i.Id == Id)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnGroupDeleted(item);
                this.context.Groups.Remove(item);
                this.context.SaveChanges();
                this.OnAfterGroupDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnGroupUpdated(WicsPlatform.Server.Models.wics.Group item);
        partial void OnAfterGroupUpdated(WicsPlatform.Server.Models.wics.Group item);

        [HttpPut("/odata/wics/Groups(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutGroup(ulong Id, [FromBody]WicsPlatform.Server.Models.wics.Group item)
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
                this.OnGroupUpdated(item);
                this.context.Groups.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Groups.Where(i => i.Id == Id);
                
                this.OnAfterGroupUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/Groups(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchGroup(ulong Id, [FromBody]Delta<WicsPlatform.Server.Models.wics.Group> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.Groups.Where(i => i.Id == Id).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnGroupUpdated(item);
                this.context.Groups.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Groups.Where(i => i.Id == Id);
                
                this.OnAfterGroupUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnGroupCreated(WicsPlatform.Server.Models.wics.Group item);
        partial void OnAfterGroupCreated(WicsPlatform.Server.Models.wics.Group item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.Group item)
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

                this.OnGroupCreated(item);
                this.context.Groups.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Groups.Where(i => i.Id == item.Id);

                

                this.OnAfterGroupCreated(item);

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
