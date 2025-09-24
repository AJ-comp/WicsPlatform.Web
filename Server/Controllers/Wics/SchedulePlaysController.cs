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
    [Route("odata/wics/SchedulePlays")]
    public partial class SchedulePlaysController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public SchedulePlaysController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.SchedulePlay> GetSchedulePlays()
        {
            var items = this.context.SchedulePlays.AsQueryable<WicsPlatform.Server.Models.wics.SchedulePlay>();
            this.OnSchedulePlaysRead(ref items);

            return items;
        }

        partial void OnSchedulePlaysRead(ref IQueryable<WicsPlatform.Server.Models.wics.SchedulePlay> items);

        partial void OnSchedulePlayGet(ref SingleResult<WicsPlatform.Server.Models.wics.SchedulePlay> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/SchedulePlays(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.SchedulePlay> GetSchedulePlay(ulong key)
        {
            var items = this.context.SchedulePlays.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnSchedulePlayGet(ref result);

            return result;
        }
        partial void OnSchedulePlayDeleted(WicsPlatform.Server.Models.wics.SchedulePlay item);
        partial void OnAfterSchedulePlayDeleted(WicsPlatform.Server.Models.wics.SchedulePlay item);

        [HttpDelete("/odata/wics/SchedulePlays(Id={Id})")]
        public IActionResult DeleteSchedulePlay(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.SchedulePlays
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnSchedulePlayDeleted(item);
                this.context.SchedulePlays.Remove(item);
                this.context.SaveChanges();
                this.OnAfterSchedulePlayDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnSchedulePlayUpdated(WicsPlatform.Server.Models.wics.SchedulePlay item);
        partial void OnAfterSchedulePlayUpdated(WicsPlatform.Server.Models.wics.SchedulePlay item);

        [HttpPut("/odata/wics/SchedulePlays(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutSchedulePlay(ulong key, [FromBody]WicsPlatform.Server.Models.wics.SchedulePlay item)
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
                this.OnSchedulePlayUpdated(item);
                this.context.SchedulePlays.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.SchedulePlays.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Schedule");
                this.OnAfterSchedulePlayUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/SchedulePlays(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchSchedulePlay(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.SchedulePlay> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.SchedulePlays.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnSchedulePlayUpdated(item);
                this.context.SchedulePlays.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.SchedulePlays.Where(i => i.Id == key);
                Request.QueryString = Request.QueryString.Add("$expand", "Schedule");
                this.OnAfterSchedulePlayUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnSchedulePlayCreated(WicsPlatform.Server.Models.wics.SchedulePlay item);
        partial void OnAfterSchedulePlayCreated(WicsPlatform.Server.Models.wics.SchedulePlay item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.SchedulePlay item)
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

                this.OnSchedulePlayCreated(item);
                this.context.SchedulePlays.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.SchedulePlays.Where(i => i.Id == item.Id);

                Request.QueryString = Request.QueryString.Add("$expand", "Schedule");

                this.OnAfterSchedulePlayCreated(item);

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
