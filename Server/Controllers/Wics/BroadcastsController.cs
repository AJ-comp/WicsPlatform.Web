using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using System.Data;
using WicsPlatform.Server.Services;

namespace WicsPlatform.Server.Controllers.wics;

[Route("odata/wics/Broadcasts")]
public partial class BroadcastsController : ODataController
{
    private WicsPlatform.Server.Data.wicsContext context;
    private readonly IScheduleExecutionService scheduleExecutionService;

    public BroadcastsController(
        WicsPlatform.Server.Data.wicsContext context,
        IScheduleExecutionService scheduleExecutionService)
    {
        this.context = context;
        this.scheduleExecutionService = scheduleExecutionService;
    }


    [HttpGet]
    [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
    public IEnumerable<WicsPlatform.Server.Models.wics.Broadcast> GetBroadcasts()
    {
        var items = this.context.Broadcasts.AsQueryable<WicsPlatform.Server.Models.wics.Broadcast>();
        this.OnBroadcastsRead(ref items);

        return items;
    }

    partial void OnBroadcastsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Broadcast> items);

    partial void OnBroadcastGet(ref SingleResult<WicsPlatform.Server.Models.wics.Broadcast> item);

    [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
    [HttpGet("/odata/wics/Broadcasts(Id={Id})")]
    public SingleResult<WicsPlatform.Server.Models.wics.Broadcast> GetBroadcast(ulong key)
    {
        var items = this.context.Broadcasts.Where(i => i.Id == key);
        var result = SingleResult.Create(items);

        OnBroadcastGet(ref result);

        return result;
    }
    partial void OnBroadcastDeleted(WicsPlatform.Server.Models.wics.Broadcast item);
    partial void OnAfterBroadcastDeleted(WicsPlatform.Server.Models.wics.Broadcast item);

    [HttpDelete("/odata/wics/Broadcasts(Id={Id})")]
    public IActionResult DeleteBroadcast(ulong key)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }


            var item = this.context.Broadcasts
                .Where(i => i.Id == key)
                .FirstOrDefault();

            if (item == null)
            {
                return BadRequest();
            }
            this.OnBroadcastDeleted(item);
            this.context.Broadcasts.Remove(item);
            this.context.SaveChanges();
            this.OnAfterBroadcastDeleted(item);

            return new NoContentResult();

        }
        catch(Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            return BadRequest(ModelState);
        }
    }

    partial void OnBroadcastUpdated(WicsPlatform.Server.Models.wics.Broadcast item);
    partial void OnAfterBroadcastUpdated(WicsPlatform.Server.Models.wics.Broadcast item);

    [HttpPut("/odata/wics/Broadcasts(Id={Id})")]
    [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
    public IActionResult PutBroadcast(ulong key, [FromBody]WicsPlatform.Server.Models.wics.Broadcast item)
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
            this.OnBroadcastUpdated(item);
            this.context.Broadcasts.Update(item);
            this.context.SaveChanges();

            var itemToReturn = this.context.Broadcasts.Where(i => i.Id == key);
            Request.QueryString = Request.QueryString.Add("$expand", "Channel");
            this.OnAfterBroadcastUpdated(item);
            return new ObjectResult(SingleResult.Create(itemToReturn));
        }
        catch(Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            return BadRequest(ModelState);
        }
    }

    [HttpPatch("/odata/wics/Broadcasts(Id={Id})")]
    [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
    public IActionResult PatchBroadcast(ulong key, [FromBody]Delta<WicsPlatform.Server.Models.wics.Broadcast> patch)
    {
        try
        {
            if(!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var item = this.context.Broadcasts.Where(i => i.Id == key).FirstOrDefault();

            if (item == null)
            {
                return BadRequest();
            }
            patch.Patch(item);

            this.OnBroadcastUpdated(item);
            this.context.Broadcasts.Update(item);
            this.context.SaveChanges();

            var itemToReturn = this.context.Broadcasts.Where(i => i.Id == key);
            Request.QueryString = Request.QueryString.Add("$expand", "Channel");
            this.OnAfterBroadcastUpdated(item);
            return new ObjectResult(SingleResult.Create(itemToReturn));
        }
        catch(Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            return BadRequest(ModelState);
        }
    }

    partial void OnBroadcastCreated(WicsPlatform.Server.Models.wics.Broadcast item);
    partial void OnAfterBroadcastCreated(WicsPlatform.Server.Models.wics.Broadcast item);

    [HttpPost]
    [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
    public IActionResult Post([FromBody] WicsPlatform.Server.Models.wics.Broadcast item)
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

            this.OnBroadcastCreated(item);
            this.context.Broadcasts.Add(item);
            this.context.SaveChanges();

            var itemToReturn = this.context.Broadcasts.Where(i => i.Id == item.Id);

            Request.QueryString = Request.QueryString.Add("$expand", "Channel");

            this.OnAfterBroadcastCreated(item);

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

    /// <summary>
    /// 진행 중인 방송을 강제로 종료합니다.
    /// </summary>
    /// <param name="broadcastId">종료할 방송 ID</param>
    /// <returns>종료 결과</returns>
    [HttpPost("/api/broadcasts/{broadcastId}/finalize")]
    public async Task<IActionResult> FinalizeBroadcast(ulong broadcastId)
    {
        try
        {
            // Broadcast 조회
            var broadcast = await context.Broadcasts
                .Include(b => b.Channel)
                .FirstOrDefaultAsync(b => b.Id == broadcastId);

            if (broadcast == null)
            {
                return NotFound(new { message = $"Broadcast with ID {broadcastId} not found." });
            }

            // 이미 종료된 방송인지 확인
            if (broadcast.OngoingYn == "N")
            {
                return BadRequest(new { message = $"Broadcast {broadcastId} is already finalized." });
            }

            // 방송 종료 실행
            await scheduleExecutionService.FinalizeBroadcastAsync(broadcastId, broadcast.ChannelId);

            return Ok(new 
            { 
                message = "Broadcast finalized successfully.",
                broadcastId = broadcastId,
                channelId = broadcast.ChannelId,
                channelName = broadcast.Channel?.Name
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new 
            { 
                message = "Failed to finalize broadcast.",
                error = ex.Message,
                broadcastId = broadcastId
            });
        }
    }
}
