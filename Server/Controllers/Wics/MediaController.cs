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
    [Route("odata/wics/Media")]
    public partial class MediaController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public MediaController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.Medium> GetMedia()
        {
            var items = this.context.Media.AsQueryable<WicsPlatform.Server.Models.wics.Medium>();
            this.OnMediaRead(ref items);

            return items;
        }

        partial void OnMediaRead(ref IQueryable<WicsPlatform.Server.Models.wics.Medium> items);

        partial void OnMediumGet(ref SingleResult<WicsPlatform.Server.Models.wics.Medium> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/Media(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.Medium> GetMedium(ulong key)
        {
            var items = this.context.Media.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnMediumGet(ref result);

            return result;
        }
        partial void OnMediumDeleted(WicsPlatform.Server.Models.wics.Medium item);
        partial void OnAfterMediumDeleted(WicsPlatform.Server.Models.wics.Medium item);

        [HttpDelete("/odata/wics/Media(Id={Id})")]
        public IActionResult DeleteMedium(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.Media
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnMediumDeleted(item);
                this.context.Media.Remove(item);
                this.context.SaveChanges();
                this.OnAfterMediumDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMediumUpdated(WicsPlatform.Server.Models.wics.Medium item);
        partial void OnAfterMediumUpdated(WicsPlatform.Server.Models.wics.Medium item);

        [HttpPut("/odata/wics/Media(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutMedium(ulong key, [FromBody]WicsPlatform.Server.Models.wics.Medium item)
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
                this.OnMediumUpdated(item);
                this.context.Media.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Media.Where(i => i.Id == key);
                
                this.OnAfterMediumUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/Media(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchMedium(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.Medium> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.Media.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnMediumUpdated(item);
                this.context.Media.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Media.Where(i => i.Id == key);
                
                this.OnAfterMediumUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnMediumCreated(WicsPlatform.Server.Models.wics.Medium item);
        partial void OnAfterMediumCreated(WicsPlatform.Server.Models.wics.Medium item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.Medium item)
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

                this.OnMediumCreated(item);
                this.context.Media.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Media.Where(i => i.Id == item.Id);

                

                this.OnAfterMediumCreated(item);

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
