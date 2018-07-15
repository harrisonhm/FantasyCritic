using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using FantasyCritic.Lib.Domain;
using FantasyCritic.Lib.Services;
using FantasyCritic.Web.Models;
using FantasyCritic.Web.Models.Requests;
using FantasyCritic.Web.Models.Responses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FantasyCritic.Web.Controllers.API
{
    [Route("api/[controller]/[action]")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class LeagueController : Controller
    {
        private readonly FantasyCriticUserManager _userManager;
        private readonly FantasyCriticService _fantasyCriticService;

        public LeagueController(FantasyCriticUserManager userManager, FantasyCriticService fantasyCriticService)
        {
            _userManager = userManager;
            _fantasyCriticService = fantasyCriticService;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetLeague(Guid id)
        {
            Maybe<FantasyCriticLeague> league = await _fantasyCriticService.GetLeagueByID(id);
            if (league.HasNoValue)
            {
                return NotFound();
            }

            var leagueViewModel = new FantasyCriticLeagueViewModel(league.Value);
            return Ok(leagueViewModel);
        }

        [HttpPost]
        public async Task<IActionResult> CreateLeague([FromBody] CreateLeagueRequest request)
        {
            var currentUser = await _userManager.FindByNameAsync(User.Identity.Name);
            if (currentUser == null)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            LeagueCreationParameters domainRequest = request.ToDomain(currentUser);
            var league = await _fantasyCriticService.CreateLeague(domainRequest);
            var viewModel = new FantasyCriticLeagueViewModel(league);

            return Ok(viewModel);
        }
    }
}
