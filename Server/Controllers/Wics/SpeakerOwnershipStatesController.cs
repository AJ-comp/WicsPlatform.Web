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
    [Route("odata/wics/SpeakerOwnershipStates")]
    public partial class SpeakerOwnershipStatesController : ODataController
    {
        private WicsPlatform.Server.Data.wicsContext context;

        public SpeakerOwnershipStatesController(WicsPlatform.Server.Data.wicsContext context)
        {
            this.context = context;
        }

    
        [HttpGet]
        [EnableQuery(MaxExpansionDepth=10,MaxAnyAllExpressionDepth=10,MaxNodeCount=1000)]
        public IEnumerable<WicsPlatform.Server.Models.wics.SpeakerOwnershipState> GetSpeakerOwnershipStates()
        {
            var items = this.context.SpeakerOwnershipStates.AsQueryable<WicsPlatform.Server.Models.wics.SpeakerOwnershipState>();
            this.OnSpeakerOwnershipStatesRead(ref items);

            return items;
        }

        partial void OnSpeakerOwnershipStatesRead(ref IQueryable<WicsPlatform.Server.Models.wics.SpeakerOwnershipState> items);
    }
}
