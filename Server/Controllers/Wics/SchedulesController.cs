using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using System.Data;

namespace WicsPlatform.Server.Controllers.wics
{
    [Route("odata/wics/Schedules")]
    public partial class SchedulesController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public SchedulesController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.Schedule> GetSchedules()
        {
            var items = this.context.Schedules.AsQueryable<WicsPlatform.Server.Models.wics.Schedule>();
            this.OnSchedulesRead(ref items);

            return items;
        }

        partial void OnSchedulesRead(ref IQueryable<WicsPlatform.Server.Models.wics.Schedule> items);

        partial void OnScheduleGet(ref SingleResult<WicsPlatform.Server.Models.wics.Schedule> item);

        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        [HttpGet("/odata/wics/Schedules(Id={Id})")]
        public SingleResult<WicsPlatform.Server.Models.wics.Schedule> GetSchedule(ulong key)
        {
            var items = this.context.Schedules.Where(i => i.Id == key);
            var result = SingleResult.Create(items);

            OnScheduleGet(ref result);

            return result;
        }
        partial void OnScheduleDeleted(WicsPlatform.Server.Models.wics.Schedule item);
        partial void OnAfterScheduleDeleted(WicsPlatform.Server.Models.wics.Schedule item);

        [HttpDelete("/odata/wics/Schedules(Id={Id})")]
        public IActionResult DeleteSchedule(ulong key)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }


                var item = this.context.Schedules
                    .Where(i => i.Id == key)
                    .FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                this.OnScheduleDeleted(item);
                this.context.Schedules.Remove(item);
                this.context.SaveChanges();
                this.OnAfterScheduleDeleted(item);

                return new NoContentResult();

            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnScheduleUpdated(WicsPlatform.Server.Models.wics.Schedule item);
        partial void OnAfterScheduleUpdated(WicsPlatform.Server.Models.wics.Schedule item);

        [HttpPut("/odata/wics/Schedules(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PutSchedule(ulong key, [FromBody]WicsPlatform.Server.Models.wics.Schedule item)
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
                this.OnScheduleUpdated(item);
                this.context.Schedules.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Schedules.Where(i => i.Id == key);
                
                this.OnAfterScheduleUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        [HttpPatch("/odata/wics/Schedules(Id={Id})")]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult PatchSchedule(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.Schedule> patch)
        {
            try
            {
                if(!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var item = this.context.Schedules.Where(i => i.Id == key).FirstOrDefault();

                if (item == null)
                {
                    return BadRequest();
                }
                patch.Patch(item);

                this.OnScheduleUpdated(item);
                this.context.Schedules.Update(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Schedules.Where(i => i.Id == key);
                
                this.OnAfterScheduleUpdated(item);
                return new ObjectResult(SingleResult.Create(itemToReturn));
            }
            catch(Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return BadRequest(ModelState);
            }
        }

        partial void OnScheduleCreated(WicsPlatform.Server.Models.wics.Schedule item);
        partial void OnAfterScheduleCreated(WicsPlatform.Server.Models.wics.Schedule item);

        [HttpPost]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.Schedule item)
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

                this.OnScheduleCreated(item);
                this.context.Schedules.Add(item);
                this.context.SaveChanges();

                var itemToReturn = this.context.Schedules.Where(i => i.Id == item.Id);

                

                this.OnAfterScheduleCreated(item);

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
