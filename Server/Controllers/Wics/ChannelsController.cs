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
    [Route("odata/wics/Channels")]
    public partial class ChannelsController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public ChannelsController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.Channel> GetChannels()
        {
            var items = this.context.Channels.AsQueryable<WicsPlatform.Server.Models.wics.Channel>();
            this.OnChannelsRead(ref items);

            return items;
        }

        partial void OnChannelsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Channel> items);

        partial void OnChannelGet(ref SingleResult<WicsPlatform.Server.Models.wics.Channel> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/Channels(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.Channel> GetChannel(ulong Id)
        {
            var items = this.context.Channels.Where(i => i.Id == Id);
            var result = SingleResult.Create(items);

            OnChannelGet(ref result);

            return result;
        }
        partial void OnChannelDeleted(WicsPlatform.Server.Models.wics.Channel item);
        partial void OnAfterChannelDeleted(WicsPlatform.Server.Models.wics.Channel item);

        [HttpDelete("/odata/wics/Channels(Id={Id})")]
        public IActionResult DeleteChannel(ulong Id)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.Channels
                    .Where(i => i.Id == Id)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnChannelDeleted(item);
                this.context.Channels.Remove(item);
                this.context.SaveChanges();
                this.OnAfterChannelDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnChannelUpdated(WicsPlatform.Server.Models.wics.Channel item);
        partial void OnAfterChannelUpdated(WicsPlatform.Server.Models.wics.Channel item);

        [HttpPut("/odata/wics/Channels(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutChannel(ulong Id, [FromBody]WicsPlatform.Server.Models.wics.Channel item)
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
                this.OnChannelUpdated(item);
                this.context.Channels.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Channels.Where(i => i.Id == Id);
                
                this.OnAfterChannelUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/Channels(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchChannel(ulong Id, [FromBody]Delta<WicsPlatform.Server.Models.wics.Channel> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.Channels.Where(i => i.Id == Id).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnChannelUpdated(item);
                this.context.Channels.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Channels.Where(i => i.Id == Id);
                
                this.OnAfterChannelUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnChannelCreated(WicsPlatform.Server.Models.wics.Channel item);
        partial void OnAfterChannelCreated(WicsPlatform.Server.Models.wics.Channel item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.Channel item)
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

                this.OnChannelCreated(item);
                this.context.Channels.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Channels.Where(i => i.Id == item.Id);

                

                this.OnAfterChannelCreated(item);

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
