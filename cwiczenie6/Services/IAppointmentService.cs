using cwiczenie6.DTOs;
namespace cwiczenie6.Services;

public interface IAppointmentService
{
    Task<List<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName);
    Task<AppointmentDetailsDto> GetAppointmentDetailsAsync(int idAppointment);
    Task<int> AddAppointmentAsync(CreateAppointmentRequestDto request);
    Task UpdateAppointmentAsync(int idAppointment, UpdateAppointmentRequestDto request);
    Task DeleteAppointmentAsync(int idAppointment);
}