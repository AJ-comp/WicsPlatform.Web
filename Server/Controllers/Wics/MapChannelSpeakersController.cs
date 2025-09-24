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
    [Route("odata/wics/MapChannelSpeakers")]
    public partial class MapChannelSpeakersController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public MapChannelSpeakersController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.MapChannelSpeaker> GetMapChannelSpeakers()
        {
            var items = this.context.MapChannelSpeakers.AsQueryable<WicsPlatform.Server.Models.wics.MapChannelSpeaker>();
            this.OnMapChannelSpeakersRead(ref items);

            return items;
        }

        partial void OnMapChannelSpeakersRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapChannelSpeaker> items);

        partial void OnMapChannelSpeakerGet(ref SingleResult<WicsPlatform.Server.Models.wics.MapChannelSpeaker> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/MapChannelSpeakers(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.MapChannelSpeaker> GetMapChannelSpeaker(ulong key)
        {
            var items = this.context.MapChannelSpeakers.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnMapChannelSpeakerGet(ref result);

            return result;
        }
        partial void OnMapChannelSpeakerDeleted(WicsPlatform.Server.Models.wics.MapChannelSpeaker item);
        partial void OnAfterMapChannelSpeakerDeleted(WicsPlatform.Server.Models.wics.MapChannelSpeaker item);

        [HttpDelete("/odata/wics/MapChannelSpeakers(Id={Id})")]
        public IActionResult DeleteMapChannelSpeaker(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.MapChannelSpeakers
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnMapChannelSpeakerDeleted(item);
                this.context.MapChannelSpeakers.Remove(item);
                this.context.SaveChanges();
                this.OnAfterMapChannelSpeakerDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapChannelSpeakerUpdated(WicsPlatform.Server.Models.wics.MapChannelSpeaker item);
        partial void OnAfterMapChannelSpeakerUpdated(WicsPlatform.Server.Models.wics.MapChannelSpeaker item);

        [HttpPut("/odata/wics/MapChannelSpeakers(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutMapChannelSpeaker(ulong key, [FromBody]WicsPlatform.Server.Models.wics.MapChannelSpeaker item)
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
                this.OnMapChannelSpeakerUpdated(item);
                this.context.MapChannelSpeakers.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelSpeakers.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Speaker");
                this.OnAfterMapChannelSpeakerUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/MapChannelSpeakers(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchMapChannelSpeaker(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.MapChannelSpeaker> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.MapChannelSpeakers.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnMapChannelSpeakerUpdated(item);
                this.context.MapChannelSpeakers.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelSpeakers.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Speaker");
                this.OnAfterMapChannelSpeakerUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapChannelSpeakerCreated(WicsPlatform.Server.Models.wics.MapChannelSpeaker item);
        partial void OnAfterMapChannelSpeakerCreated(WicsPlatform.Server.Models.wics.MapChannelSpeaker item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.MapChannelSpeaker item)
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

                this.OnMapChannelSpeakerCreated(item);
                this.context.MapChannelSpeakers.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelSpeakers.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Speaker");

                this.OnAfterMapChannelSpeakerCreated(item);

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
