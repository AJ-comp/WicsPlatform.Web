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
    [Route("odata/wics/Mics")]
    public partial class MicsController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public MicsController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.Mic> GetMics()
        {
            var items = this.context.Mics.AsQueryable<WicsPlatform.Server.Models.wics.Mic>();
            this.OnMicsRead(ref items);

            return items;
        }

        partial void OnMicsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Mic> items);

        partial void OnMicGet(ref SingleResult<WicsPlatform.Server.Models.wics.Mic> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/Mics(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.Mic> GetMic(string key)
        {
            var items = this.context.Mics.Where(i => i.Id == Uri.UnescapeDataString(key));
            var result = SingleResult.Create(items);

            OnMicGet(ref result);

            return result;
        }
        partial void OnMicDeleted(WicsPlatform.Server.Models.wics.Mic item);
        partial void OnAfterMicDeleted(WicsPlatform.Server.Models.wics.Mic item);

        [HttpDelete("/odata/wics/Mics(Id={Id})")]
        public IActionResult DeleteMic(string key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.Mics
                    .Where(i => i.Id == Uri.UnescapeDataString(key))
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnMicDeleted(item);
                this.context.Mics.Remove(item);
                this.context.SaveChanges();
                this.OnAfterMicDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMicUpdated(WicsPlatform.Server.Models.wics.Mic item);
        partial void OnAfterMicUpdated(WicsPlatform.Server.Models.wics.Mic item);

        [HttpPut("/odata/wics/Mics(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutMic(string key, [FromBody]WicsPlatform.Server.Models.wics.Mic item)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                if (item == null || (item.Id != Uri.UnescapeDataString(key)))
                {
                    return BadRequest();
                }
                this.OnMicUpdated(item);
                this.context.Mics.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Mics.Where(i => i.Id == Uri.UnescapeDataString(key));
                
                this.OnAfterMicUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/Mics(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchMic(string key, [FromBody]Delta<WicsPlatform.Server.Models.wics.Mic> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.Mics.Where(i => i.Id == Uri.UnescapeDataString(key)).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnMicUpdated(item);
                this.context.Mics.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Mics.Where(i => i.Id == Uri.UnescapeDataString(key));
                
                this.OnAfterMicUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMicCreated(WicsPlatform.Server.Models.wics.Mic item);
        partial void OnAfterMicCreated(WicsPlatform.Server.Models.wics.Mic item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.Mic item)
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

                this.OnMicCreated(item);
                this.context.Mics.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Mics.Where(i => i.Id == item.Id);

                

                this.OnAfterMicCreated(item);

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
