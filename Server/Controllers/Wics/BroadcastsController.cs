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
    [Route("odata/wics/Broadcasts")]
    public partial class BroadcastsController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public BroadcastsController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.Broadcast> GetBroadcasts()
        {
            var items = this.context.Broadcasts.AsQueryable<WicsPlatform.Server.Models.wics.Broadcast>();
            this.OnBroadcastsRead(ref items);

            return items;
        }

        partial void OnBroadcastsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Broadcast> items);

        partial void OnBroadcastGet(ref SingleResult<WicsPlatform.Server.Models.wics.Broadcast> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/Broadcasts(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.Broadcast> GetBroadcast(ulong Id)
        {
            var items = this.context.Broadcasts.Where(i => i.Id == Id);
            var result = SingleResult.Create(items);

            OnBroadcastGet(ref result);

            return result;
        }
        partial void OnBroadcastDeleted(WicsPlatform.Server.Models.wics.Broadcast item);
        partial void OnAfterBroadcastDeleted(WicsPlatform.Server.Models.wics.Broadcast item);

        [HttpDelete("/odata/wics/Broadcasts(Id={Id})")]
        public IActionResult DeleteBroadcast(ulong Id)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.Broadcasts
                    .Where(i => i.Id == Id)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnBroadcastDeleted(item);
                this.context.Broadcasts.Remove(item);
                this.context.SaveChanges();
                this.OnAfterBroadcastDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnBroadcastUpdated(WicsPlatform.Server.Models.wics.Broadcast item);
        partial void OnAfterBroadcastUpdated(WicsPlatform.Server.Models.wics.Broadcast item);

        [HttpPut("/odata/wics/Broadcasts(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutBroadcast(ulong Id, [FromBody]WicsPlatform.Server.Models.wics.Broadcast item)
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
                this.OnBroadcastUpdated(item);
                this.context.Broadcasts.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Broadcasts.Where(i => i.Id == Id);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Medium,Speaker");
                this.OnAfterBroadcastUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/Broadcasts(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchBroadcast(ulong Id, [FromBody]Delta<WicsPlatform.Server.Models.wics.Broadcast> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.Broadcasts.Where(i => i.Id == Id).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnBroadcastUpdated(item);
                this.context.Broadcasts.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Broadcasts.Where(i => i.Id == Id);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Medium,Speaker");
                this.OnAfterBroadcastUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnBroadcastCreated(WicsPlatform.Server.Models.wics.Broadcast item);
        partial void OnAfterBroadcastCreated(WicsPlatform.Server.Models.wics.Broadcast item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.Broadcast item)
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

                this.OnBroadcastCreated(item);
                this.context.Broadcasts.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Broadcasts.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Medium,Speaker");

                this.OnAfterBroadcastCreated(item);

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
