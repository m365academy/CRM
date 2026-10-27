using M365AcademyCrm.Authentication;
using M365AcademyCrm.Controllers;
using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Areas.Admin.Controllers;

/// <summary>The sales catalogue: courses and list prices used to value deals and build quotes.</summary>
[Area("Admin")]
[Authorize(Roles = CrmRoles.Admin)]
[Route("admin/courses")]
public class CoursesController : CrmController
{
    private readonly CrmDbContext _db;

    public CoursesController(CrmDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Admin: Course catalogue | M365 Academy CRM";
        return View(await _db.Courses.AsNoTracking().OrderBy(c => c.Category).ThenBy(c => c.Code).ToListAsync(cancellationToken));
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save(Course input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            FlashError("Code and title are required, and prices can't be negative.");
            return RedirectToAction(nameof(Index));
        }

        var code = input.Code.Trim();
        if (await _db.Courses.AnyAsync(c => c.Code == code && c.Id != input.Id, cancellationToken))
        {
            FlashError($"There's already a course with code {code}.");
            return RedirectToAction(nameof(Index));
        }

        var course = input.Id == 0 ? new Course() : await _db.Courses.FirstOrDefaultAsync(c => c.Id == input.Id, cancellationToken);
        if (course is null)
        {
            return RedirectToAction(nameof(Index));
        }

        course.Code = code;
        course.Title = input.Title.Trim();
        course.Category = input.Category;
        course.Duration = Clean(input.Duration);
        course.PricePerDelegate = input.PricePerDelegate;
        course.PrivateCoursePrice = input.PrivateCoursePrice;
        course.IsActive = input.IsActive;
        if (course.Id == 0)
        {
            _db.Courses.Add(course);
        }

        await _db.SaveChangesAsync(cancellationToken);
        Flash($"{course.Code} saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var inUse = await _db.Deals.AnyAsync(d => d.CourseId == id, cancellationToken)
            || await _db.Leads.AnyAsync(l => l.CourseId == id, cancellationToken)
            || await _db.QuoteLines.AnyAsync(l => l.CourseId == id, cancellationToken);
        if (inUse)
        {
            FlashError($"{course.Code} is used by existing leads, deals or quotes, so it can't be deleted - untick \"Active\" to retire it instead.");
            return RedirectToAction(nameof(Index));
        }

        _db.Courses.Remove(course);
        await _db.SaveChangesAsync(cancellationToken);
        Flash($"{course.Code} deleted.");
        return RedirectToAction(nameof(Index));
    }
}
