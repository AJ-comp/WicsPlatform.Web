using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

using WicsPlatform.Server.Data;

namespace WicsPlatform.Server.Controllers
{
    public partial class ExportwicsController : ExportController
    {
        private readonly wicsContext context;
        private readonly wicsService service;

        public ExportwicsController(wicsContext context, wicsService service)
        {
            this.service = service;
            this.context = context;
        }

        [HttpGet("/export/wics/broadcasts/csv")]
        [HttpGet("/export/wics/broadcasts/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportBroadcastsToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetBroadcasts(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/broadcasts/excel")]
        [HttpGet("/export/wics/broadcasts/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportBroadcastsToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetBroadcasts(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/channels/csv")]
        [HttpGet("/export/wics/channels/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportChannelsToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetChannels(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/channels/excel")]
        [HttpGet("/export/wics/channels/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportChannelsToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetChannels(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/groups/csv")]
        [HttpGet("/export/wics/groups/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportGroupsToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetGroups(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/groups/excel")]
        [HttpGet("/export/wics/groups/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportGroupsToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetGroups(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/mapchannelmedia/csv")]
        [HttpGet("/export/wics/mapchannelmedia/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMapChannelMediaToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetMapChannelMedia(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/mapchannelmedia/excel")]
        [HttpGet("/export/wics/mapchannelmedia/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMapChannelMediaToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetMapChannelMedia(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/mapchanneltts/csv")]
        [HttpGet("/export/wics/mapchanneltts/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMapChannelTtsToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetMapChannelTts(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/mapchanneltts/excel")]
        [HttpGet("/export/wics/mapchanneltts/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMapChannelTtsToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetMapChannelTts(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/mapmediagroups/csv")]
        [HttpGet("/export/wics/mapmediagroups/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMapMediaGroupsToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetMapMediaGroups(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/mapmediagroups/excel")]
        [HttpGet("/export/wics/mapmediagroups/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMapMediaGroupsToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetMapMediaGroups(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/mapspeakergroups/csv")]
        [HttpGet("/export/wics/mapspeakergroups/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMapSpeakerGroupsToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetMapSpeakerGroups(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/mapspeakergroups/excel")]
        [HttpGet("/export/wics/mapspeakergroups/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMapSpeakerGroupsToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetMapSpeakerGroups(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/media/csv")]
        [HttpGet("/export/wics/media/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMediaToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetMedia(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/media/excel")]
        [HttpGet("/export/wics/media/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMediaToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetMedia(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/mics/csv")]
        [HttpGet("/export/wics/mics/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMicsToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetMics(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/mics/excel")]
        [HttpGet("/export/wics/mics/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportMicsToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetMics(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/speakers/csv")]
        [HttpGet("/export/wics/speakers/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportSpeakersToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetSpeakers(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/speakers/excel")]
        [HttpGet("/export/wics/speakers/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportSpeakersToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetSpeakers(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/tts/csv")]
        [HttpGet("/export/wics/tts/csv(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportTtsToCSV(string fileName = null)
        {
            return ToCSV(ApplyQuery(await service.GetTts(), Request.Query, false), fileName);
        }

        [HttpGet("/export/wics/tts/excel")]
        [HttpGet("/export/wics/tts/excel(fileName='{fileName}')")]
        public async Task<FileStreamResult> ExportTtsToExcel(string fileName = null)
        {
            return ToExcel(ApplyQuery(await service.GetTts(), Request.Query, false), fileName);
        }
    }
}
