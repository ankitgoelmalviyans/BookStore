using BookStore.AiService.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.AiService.API.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly IBookSearchService _searchService;

    public SearchController(IBookSearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest("Query is required.");
        }

        var result = await _searchService.SearchAsync(request.Query, request.TopK ?? 5);
        return Ok(result);
    }
}

public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int? TopK { get; set; }
}
