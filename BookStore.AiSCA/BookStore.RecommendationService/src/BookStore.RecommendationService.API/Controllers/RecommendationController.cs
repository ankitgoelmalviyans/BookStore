using BookStore.RecommendationService.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.RecommendationService.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecommendationController : ControllerBase
{
    private readonly IRecommendationService _recommendationService;

    public RecommendationController(IRecommendationService recommendationService)
    {
        _recommendationService = recommendationService;
    }

    [HttpGet("{productId}")]
    public async Task<IActionResult> GetRecommendations(Guid productId, [FromQuery] int top = 5)
    {
        var recommendations = await _recommendationService.GetRecommendationsAsync(productId, top);
        return Ok(recommendations);
    }
}
