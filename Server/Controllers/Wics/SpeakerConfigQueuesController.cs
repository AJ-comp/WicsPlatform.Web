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
    [Route("odata/wics/SpeakerConfigQueues")]
    public partial class SpeakerConfigQueuesController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public SpeakerConfigQueuesController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.SpeakerConfigQueue> GetSpeakerConfigQueues()
        {
            var items = this.context.SpeakerConfigQueues.AsQueryable<WicsPlatform.Server.Models.wics.SpeakerConfigQueue>();
            this.OnSpeakerConfigQueuesRead(ref items);

            return items;
        }

        partial void OnSpeakerConfigQueuesRead(ref IQueryable<WicsPlatform.Server.Models.wics.SpeakerConfigQueue> items);

        partial void OnSpeakerConfigQueueGet(ref SingleResult<WicsPlatform.Server.Models.wics.SpeakerConfigQueue> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/SpeakerConfigQueues(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.SpeakerConfigQueue> GetSpeakerConfigQueue(ulong key)
        {
            var items = this.context.SpeakerConfigQueues.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnSpeakerConfigQueueGet(ref result);

            return result;
        }
        partial void OnSpeakerConfigQueueDeleted(WicsPlatform.Server.Models.wics.SpeakerConfigQueue item);
        partial void OnAfterSpeakerConfigQueueDeleted(WicsPlatform.Server.Models.wics.SpeakerConfigQueue item);

        [HttpDelete("/odata/wics/SpeakerConfigQueues(Id={Id})")]
        public IActionResult DeleteSpeakerConfigQueue(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.SpeakerConfigQueues
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnSpeakerConfigQueueDeleted(item);
                this.context.SpeakerConfigQueues.Remove(item);
                this.context.SaveChanges();
                this.OnAfterSpeakerConfigQueueDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnSpeakerConfigQueueUpdated(WicsPlatform.Server.Models.wics.SpeakerConfigQueue item);
        partial void OnAfterSpeakerConfigQueueUpdated(WicsPlatform.Server.Models.wics.SpeakerConfigQueue item);

        [HttpPut("/odata/wics/SpeakerConfigQueues(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutSpeakerConfigQueue(ulong key, [FromBody]WicsPlatform.Server.Models.wics.SpeakerConfigQueue item)
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
                this.OnSpeakerConfigQueueUpdated(item);
                this.context.SpeakerConfigQueues.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.SpeakerConfigQueues.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Speaker");
                this.OnAfterSpeakerConfigQueueUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/SpeakerConfigQueues(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchSpeakerConfigQueue(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.SpeakerConfigQueue> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.SpeakerConfigQueues.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnSpeakerConfigQueueUpdated(item);
                this.context.SpeakerConfigQueues.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.SpeakerConfigQueues.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Speaker");
                this.OnAfterSpeakerConfigQueueUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnSpeakerConfigQueueCreated(WicsPlatform.Server.Models.wics.SpeakerConfigQueue item);
        partial void OnAfterSpeakerConfigQueueCreated(WicsPlatform.Server.Models.wics.SpeakerConfigQueue item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.SpeakerConfigQueue item)
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

                this.OnSpeakerConfigQueueCreated(item);
                this.context.SpeakerConfigQueues.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.SpeakerConfigQueues.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Channel,Speaker");

                this.OnAfterSpeakerConfigQueueCreated(item);

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
