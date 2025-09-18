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
    [Route("odata/wics/MapChannelMedia")]
    public partial class MapChannelMediaController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public MapChannelMediaController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.MapChannelMedium> GetMapChannelMedia()
        {
            var items = this.context.MapChannelMedia.AsQueryable<WicsPlatform.Server.Models.wics.MapChannelMedium>();
            this.OnMapChannelMediaRead(ref items);

            return items;
        }

        partial void OnMapChannelMediaRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapChannelMedium> items);

        partial void OnMapChannelMediumGet(ref SingleResult<WicsPlatform.Server.Models.wics.MapChannelMedium> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/MapChannelMedia(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.MapChannelMedium> GetMapChannelMedium(ulong key)
        {
            var items = this.context.MapChannelMedia.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnMapChannelMediumGet(ref result);

            return result;
        }
        partial void OnMapChannelMediumDeleted(WicsPlatform.Server.Models.wics.MapChannelMedium item);
        partial void OnAfterMapChannelMediumDeleted(WicsPlatform.Server.Models.wics.MapChannelMedium item);

        [HttpDelete("/odata/wics/MapChannelMedia(Id={Id})")]
        public IActionResult DeleteMapChannelMedium(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.MapChannelMedia
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnMapChannelMediumDeleted(item);
                this.context.MapChannelMedia.Remove(item);
                this.context.SaveChanges();
                this.OnAfterMapChannelMediumDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapChannelMediumUpdated(WicsPlatform.Server.Models.wics.MapChannelMedium item);
        partial void OnAfterMapChannelMediumUpdated(WicsPlatform.Server.Models.wics.MapChannelMedium item);

        [HttpPut("/odata/wics/MapChannelMedia(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutMapChannelMedium(ulong key, [FromBody]WicsPlatform.Server.Models.wics.MapChannelMedium item)
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
                this.OnMapChannelMediumUpdated(item);
                this.context.MapChannelMedia.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelMedia.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Medium");
                this.OnAfterMapChannelMediumUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/MapChannelMedia(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchMapChannelMedium(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.MapChannelMedium> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.MapChannelMedia.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnMapChannelMediumUpdated(item);
                this.context.MapChannelMedia.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelMedia.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Medium");
                this.OnAfterMapChannelMediumUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMapChannelMediumCreated(WicsPlatform.Server.Models.wics.MapChannelMedium item);
        partial void OnAfterMapChannelMediumCreated(WicsPlatform.Server.Models.wics.MapChannelMedium item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.MapChannelMedium item)
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

                this.OnMapChannelMediumCreated(item);
                this.context.MapChannelMedia.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.MapChannelMedia.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Medium");

                this.OnAfterMapChannelMediumCreated(item);

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
