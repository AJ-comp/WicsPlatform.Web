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
    [Route("odata/wics/Tts")]
    public partial class TtsController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public TtsController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.Tt> GetTts()
        {
            var items = this.context.Tts.AsQueryable<WicsPlatform.Server.Models.wics.Tt>();
            this.OnTtsRead(ref items);

            return items;
        }

        partial void OnTtsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Tt> items);

        partial void OnTtGet(ref SingleResult<WicsPlatform.Server.Models.wics.Tt> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/Tts(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.Tt> GetTt(ulong key)
        {
            var items = this.context.Tts.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnTtGet(ref result);

            return result;
        }
        partial void OnTtDeleted(WicsPlatform.Server.Models.wics.Tt item);
        partial void OnAfterTtDeleted(WicsPlatform.Server.Models.wics.Tt item);

        [HttpDelete("/odata/wics/Tts(Id={Id})")]
        public IActionResult DeleteTt(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.Tts
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnTtDeleted(item);
                this.context.Tts.Remove(item);
                this.context.SaveChanges();
                this.OnAfterTtDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnTtUpdated(WicsPlatform.Server.Models.wics.Tt item);
        partial void OnAfterTtUpdated(WicsPlatform.Server.Models.wics.Tt item);

        [HttpPut("/odata/wics/Tts(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutTt(ulong key, [FromBody]WicsPlatform.Server.Models.wics.Tt item)
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
                this.OnTtUpdated(item);
                this.context.Tts.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Tts.Where(i => i.Id == key);
                
                this.OnAfterTtUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/Tts(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchTt(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.Tt> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.Tts.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnTtUpdated(item);
                this.context.Tts.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Tts.Where(i => i.Id == key);
                
                this.OnAfterTtUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnTtCreated(WicsPlatform.Server.Models.wics.Tt item);
        partial void OnAfterTtCreated(WicsPlatform.Server.Models.wics.Tt item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.Tt item)
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

                this.OnTtCreated(item);
                this.context.Tts.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Tts.Where(i => i.Id == item.Id);

                

                this.OnAfterTtCreated(item);

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
