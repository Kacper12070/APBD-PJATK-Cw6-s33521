using cwiczenie6.Services;
using Microsoft.AspNetCore.Mvc;
using cwiczenie6.DTOs;

namespace cwiczenie6.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentController(IAppointmentService appointmentService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAppointments(string? status,string? patientLastName)
    {
        var appointments = await appointmentService.GetAppointmentsAsync(status, patientLastName);
        return Ok(appointments);
    }
    
    [HttpGet("{idAppointment}")]
    public async Task<IActionResult> GetAppointmentDetails(int idAppointment)
    {
        try
        {
            var details = await appointmentService.GetAppointmentDetailsAsync(idAppointment);
            return Ok(details);
        }
        catch (Exception ex)
        {
            
            return NotFound(new ErrorResponseDto { StatusCode = 404, Message = ex.Message });
        }
    }
    
    [HttpPost]
    public async Task<IActionResult> AddAppointment([FromBody] CreateAppointmentRequestDto request)
    {
        try
        {
            var newId = await appointmentService.AddAppointmentAsync(request);
            return CreatedAtAction(nameof(GetAppointmentDetails), new { idAppointment = newId }, null);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponseDto { StatusCode = 400, Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponseDto { StatusCode = 409, Message = ex.Message });
        }
    }
    
    [HttpPut("{idAppointment}")]
    public async Task<IActionResult> UpdateAppointment(int idAppointment, [FromBody] UpdateAppointmentRequestDto request)
    {
        try
        {
            await appointmentService.UpdateAppointmentAsync(idAppointment, request);
            return Ok();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponseDto { StatusCode = 404, Message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponseDto { StatusCode = 400, Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponseDto { StatusCode = 409, Message = ex.Message });
        }
    }
    
    [HttpDelete("{idAppointment}")]
    public async Task<IActionResult> DeleteAppointment(int idAppointment)
    {
        try
        {
            await appointmentService.DeleteAppointmentAsync(idAppointment);
            
            return NoContent(); 
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponseDto { StatusCode = 404, Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponseDto { StatusCode = 409, Message = ex.Message });
        }
    }
}