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
    [Route("odata/wics/Speakers")]
    public partial class SpeakersController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public SpeakersController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.Speaker> GetSpeakers()
        {
            var items = this.context.Speakers.AsQueryable<WicsPlatform.Server.Models.wics.Speaker>();
            this.OnSpeakersRead(ref items);

            return items;
        }

        partial void OnSpeakersRead(ref IQueryable<WicsPlatform.Server.Models.wics.Speaker> items);

        partial void OnSpeakerGet(ref SingleResult<WicsPlatform.Server.Models.wics.Speaker> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/Speakers(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.Speaker> GetSpeaker(ulong key)
        {
            var items = this.context.Speakers.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnSpeakerGet(ref result);

            return result;
        }
        partial void OnSpeakerDeleted(WicsPlatform.Server.Models.wics.Speaker item);
        partial void OnAfterSpeakerDeleted(WicsPlatform.Server.Models.wics.Speaker item);

        [HttpDelete("/odata/wics/Speakers(Id={Id})")]
        public IActionResult DeleteSpeaker(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.Speakers
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnSpeakerDeleted(item);
                this.context.Speakers.Remove(item);
                this.context.SaveChanges();
                this.OnAfterSpeakerDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnSpeakerUpdated(WicsPlatform.Server.Models.wics.Speaker item);
        partial void OnAfterSpeakerUpdated(WicsPlatform.Server.Models.wics.Speaker item);

        [HttpPut("/odata/wics/Speakers(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutSpeaker(ulong key, [FromBody]WicsPlatform.Server.Models.wics.Speaker item)
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
                this.OnSpeakerUpdated(item);
                this.context.Speakers.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Speakers.Where(i => i.Id == key);
                
                this.OnAfterSpeakerUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/Speakers(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchSpeaker(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.Speaker> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.Speakers.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnSpeakerUpdated(item);
                this.context.Speakers.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Speakers.Where(i => i.Id == key);
                
                this.OnAfterSpeakerUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnSpeakerCreated(WicsPlatform.Server.Models.wics.Speaker item);
        partial void OnAfterSpeakerCreated(WicsPlatform.Server.Models.wics.Speaker item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.Speaker item)
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

                this.OnSpeakerCreated(item);
                this.context.Speakers.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Speakers.Where(i => i.Id == item.Id);

                

                this.OnAfterSpeakerCreated(item);

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
