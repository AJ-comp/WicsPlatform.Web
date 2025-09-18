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
    [Route("odata/wics/SpeakerOwnershipStates")]
    public partial class SpeakerOwnershipStatesController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public SpeakerOwnershipStatesController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.SpeakerOwnershipState> GetSpeakerOwnershipStates()
        {
            var items = this.context.SpeakerOwnershipStates.AsQueryable<WicsPlatform.Server.Models.wics.SpeakerOwnershipState>();
            this.OnSpeakerOwnershipStatesRead(ref items);

            return items;
        }

        partial void OnSpeakerOwnershipStatesRead(ref IQueryable<WicsPlatform.Server.Models.wics.SpeakerOwnershipState> items);

        partial void OnSpeakerOwnershipStateGet(ref SingleResult<WicsPlatform.Server.Models.wics.SpeakerOwnershipState> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/SpeakerOwnershipStates(SpeakerId={keySpeakerId},ChannelId={keyChannelId})")]
        public SingleResult<WicsPlatform.Server.Models.wics.SpeakerOwnershipState> GetSpeakerOwnershipState([FromODataUri] ulong keySpeakerId, [FromODataUri] ulong keyChannelId)
        {
            var items = this.context.SpeakerOwnershipStates.Where(i => i.SpeakerId == keySpeakerId && i.ChannelId == keyChannelId);
            var result = SingleResult.Create(items);

            OnSpeakerOwnershipStateGet(ref result);

            return result;
        }
        partial void OnSpeakerOwnershipStateDeleted(WicsPlatform.Server.Models.wics.SpeakerOwnershipState item);
        partial void OnAfterSpeakerOwnershipStateDeleted(WicsPlatform.Server.Models.wics.SpeakerOwnershipState item);

        [HttpDelete("/odata/wics/SpeakerOwnershipStates(SpeakerId={keySpeakerId},ChannelId={keyChannelId})")]
        public IActionResult DeleteSpeakerOwnershipState([FromODataUri] ulong keySpeakerId, [FromODataUri] ulong keyChannelId)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.SpeakerOwnershipStates
                    .Where(i => i.SpeakerId == keySpeakerId && i.ChannelId == keyChannelId)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnSpeakerOwnershipStateDeleted(item);
                this.context.SpeakerOwnershipStates.Remove(item);
                this.context.SaveChanges();
                this.OnAfterSpeakerOwnershipStateDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnSpeakerOwnershipStateUpdated(WicsPlatform.Server.Models.wics.SpeakerOwnershipState item);
        partial void OnAfterSpeakerOwnershipStateUpdated(WicsPlatform.Server.Models.wics.SpeakerOwnershipState item);

        [HttpPut("/odata/wics/SpeakerOwnershipStates(SpeakerId={keySpeakerId},ChannelId={keyChannelId})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutSpeakerOwnershipState([FromODataUri] ulong keySpeakerId, [FromODataUri] ulong keyChannelId, [FromBody]WicsPlatform.Server.Models.wics.SpeakerOwnershipState item)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                if (item == null || (item.SpeakerId != keySpeakerId && item.ChannelId != keyChannelId))
                {
                    return BadRequest();
                }
                this.OnSpeakerOwnershipStateUpdated(item);
                this.context.SpeakerOwnershipStates.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.SpeakerOwnershipStates.Where(i => i.SpeakerId == keySpeakerId && i.ChannelId == keyChannelId);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Speaker");
                this.OnAfterSpeakerOwnershipStateUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/SpeakerOwnershipStates(SpeakerId={keySpeakerId},ChannelId={keyChannelId})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchSpeakerOwnershipState([FromODataUri] ulong keySpeakerId, [FromODataUri] ulong keyChannelId, [FromBody]Delta<WicsPlatform.Server.Models.wics.SpeakerOwnershipState> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.SpeakerOwnershipStates.Where(i => i.SpeakerId == keySpeakerId && i.ChannelId == keyChannelId).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnSpeakerOwnershipStateUpdated(item);
                this.context.SpeakerOwnershipStates.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.SpeakerOwnershipStates.Where(i => i.SpeakerId == keySpeakerId && i.ChannelId == keyChannelId);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Speaker");
                this.OnAfterSpeakerOwnershipStateUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnSpeakerOwnershipStateCreated(WicsPlatform.Server.Models.wics.SpeakerOwnershipState item);
        partial void OnAfterSpeakerOwnershipStateCreated(WicsPlatform.Server.Models.wics.SpeakerOwnershipState item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.SpeakerOwnershipState item)
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

                this.OnSpeakerOwnershipStateCreated(item);
                this.context.SpeakerOwnershipStates.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.SpeakerOwnershipStates.Where(i => i.SpeakerId == item.SpeakerId && i.ChannelId == item.ChannelId);

                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Speaker");

                this.OnAfterSpeakerOwnershipStateCreated(item);

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
