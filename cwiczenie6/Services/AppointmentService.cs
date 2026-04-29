using cwiczenie6.DTOs;
using Microsoft.Data.SqlClient;

namespace cwiczenie6.Services;

public class AppointmentService(IConfiguration configuration) : IAppointmentService
{
    public async Task<List<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();
        
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await using var command = new SqlCommand();

        await connection.OpenAsync();

        command.Connection = connection;

        command.CommandText = """
                              SELECT
                                  a.IdAppointment,
                                  a.AppointmentDate,
                                  a.Status,
                                  a.Reason,
                                  p.FirstName + N' ' + p.LastName AS PatientFullName,
                                  p.Email AS PatientEmail
                              FROM dbo.Appointments a
                              JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                              WHERE (@Status IS NULL OR a.Status = @Status)
                                AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
                              ORDER BY a.AppointmentDate
                              """;

        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@PatientLastName", (object?)patientLastName ?? DBNull.Value);
        
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Reason = reader.GetString(3),
                PatientFullName = reader.GetString(4),
                PatientEmail = reader.GetString(5)
            });
        }

        return appointments;
    }

    public async Task<AppointmentDetailsDto> GetAppointmentDetailsAsync(int idAppointment)
    {
        AppointmentDetailsDto? dto = null;
        
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await using var command = new SqlCommand();

        await connection.OpenAsync();

        command.Connection = connection;

        command.CommandText = """
                              SELECT 
                                  a.IdAppointment, a.AppointmentDate, a.Status, a.Reason, a.InternalNotes, a.CreatedAt,
                                  p.FirstName + N' ' + p.LastName AS PatientFullName, p.Email AS PatientEmail, p.PhoneNumber AS PatientPhoneNumber,
                                  d.FirstName + N' ' + d.LastName AS DoctorFullName, d.LicenseNumber AS DoctorLicenseNumber
                              FROM dbo.Appointments a
                              JOIN dbo.Patients p ON a.IdPatient = p.IdPatient
                              JOIN dbo.Doctors d ON a.IdDoctor = d.IdDoctor
                              WHERE a.IdAppointment = @idAppointment
                              """;
        
        command.Parameters.AddWithValue("@idAppointment", idAppointment);
        await using var reader = await command.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            dto = new AppointmentDetailsDto
            {
                IdAppointment = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Reason = reader.GetString(3),
                InternalNotes = reader.IsDBNull(4) ? null : reader.GetString(4), 
                CreatedAt = reader.GetDateTime(5),
                PatientFullName = reader.GetString(6),
                PatientEmail = reader.GetString(7),
                PatientPhoneNumber = reader.GetString(8),
                DoctorFullName = reader.GetString(9),
                DoctorLicenseNumber = reader.GetString(10)
            };
        }

        return dto;
    }
    
    public async Task<int> AddAppointmentAsync(CreateAppointmentRequestDto request)
{
    if (request.AppointmentDate < DateTime.Now)
    {
        throw new ArgumentException("Termin wizyty nie może być w przeszłości.");
    }

    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
    {
        throw new ArgumentException("Opis wizyty nie może być pusty i musi mieć maksymalnie 250 znaków.");
    }

    await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
    await connection.OpenAsync();

    await using var checkPatientCmd = new SqlCommand("SELECT 1 FROM Patients WHERE IdPatient = @id AND IsActive = 1", connection);
    checkPatientCmd.Parameters.AddWithValue("@id", request.IdPatient);
    if (await checkPatientCmd.ExecuteScalarAsync() == null)
    {
        throw new ArgumentException($"Aktywny pacjent o ID {request.IdPatient} nie istnieje.");
    }

    await using var checkDoctorCmd = new SqlCommand("SELECT 1 FROM Doctors WHERE IdDoctor = @id AND IsActive = 1", connection);
    checkDoctorCmd.Parameters.AddWithValue("@id", request.IdDoctor);
    if (await checkDoctorCmd.ExecuteScalarAsync() == null)
    {
        throw new ArgumentException($"Aktywny lekarz o ID {request.IdDoctor} nie istnieje.");
    }

    await using var checkConflictCmd = new SqlCommand(
        "SELECT 1 FROM Appointments WHERE IdDoctor = @idDoctor AND AppointmentDate = @date", connection);
    checkConflictCmd.Parameters.AddWithValue("@idDoctor", request.IdDoctor);
    checkConflictCmd.Parameters.AddWithValue("@date", request.AppointmentDate);
    
    if (await checkConflictCmd.ExecuteScalarAsync() != null)
    {
        throw new InvalidOperationException("Lekarz ma już zaplanowaną wizytę w tym terminie.");
    }

    await using var insertCmd = new SqlCommand("""
        INSERT INTO Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
        OUTPUT INSERTED.IdAppointment
        VALUES (@patient, @doctor, @date, 'Scheduled', @reason);
        """, connection);

    insertCmd.Parameters.AddWithValue("@patient", request.IdPatient);
    insertCmd.Parameters.AddWithValue("@doctor", request.IdDoctor);
    insertCmd.Parameters.AddWithValue("@date", request.AppointmentDate);
    insertCmd.Parameters.AddWithValue("@reason", request.Reason);

    var newId = (int)await insertCmd.ExecuteScalarAsync();

    return newId;
}
    public async Task UpdateAppointmentAsync(int idAppointment, UpdateAppointmentRequestDto request)
{
    var validStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
    if (!validStatuses.Contains(request.Status))
    {
        throw new ArgumentException("Niedozwolony status wizyty.");
    }

    await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
    await connection.OpenAsync();

    await using var getOldAppointmentCmd = new SqlCommand(
        "SELECT Status, AppointmentDate FROM Appointments WHERE IdAppointment = @id", connection);
    getOldAppointmentCmd.Parameters.AddWithValue("@id", idAppointment);

    await using var reader = await getOldAppointmentCmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new KeyNotFoundException($"Wizyta o ID {idAppointment} nie istnieje.");
    }

    var oldStatus = reader.GetString(0);
    var oldDate = reader.GetDateTime(1);
    await reader.CloseAsync();

    if (oldStatus == "Completed" && oldDate != request.AppointmentDate)
    {
        throw new InvalidOperationException("Nie można zmienić terminu wizyty, która została już zakończona.");
    }

    await using var checkPatientCmd = new SqlCommand("SELECT 1 FROM Patients WHERE IdPatient = @id AND IsActive = 1", connection);
    checkPatientCmd.Parameters.AddWithValue("@id", request.IdPatient);
    if (await checkPatientCmd.ExecuteScalarAsync() == null)
    {
        throw new ArgumentException($"Aktywny pacjent o ID {request.IdPatient} nie istnieje.");
    }

    await using var checkDoctorCmd = new SqlCommand("SELECT 1 FROM Doctors WHERE IdDoctor = @id AND IsActive = 1", connection);
    checkDoctorCmd.Parameters.AddWithValue("@id", request.IdDoctor);
    if (await checkDoctorCmd.ExecuteScalarAsync() == null)
    {
        throw new ArgumentException($"Aktywny lekarz o ID {request.IdDoctor} nie istnieje.");
    }

    if (oldDate != request.AppointmentDate)
    {
        await using var checkConflictCmd = new SqlCommand(
            "SELECT 1 FROM Appointments WHERE IdDoctor = @idDoctor AND AppointmentDate = @date AND IdAppointment != @id", connection);
        checkConflictCmd.Parameters.AddWithValue("@idDoctor", request.IdDoctor);
        checkConflictCmd.Parameters.AddWithValue("@date", request.AppointmentDate);
        checkConflictCmd.Parameters.AddWithValue("@id", idAppointment);

        if (await checkConflictCmd.ExecuteScalarAsync() != null)
        {
            throw new InvalidOperationException("Lekarz ma już zaplanowaną wizytę w tym terminie.");
        }
    }

    await using var updateCmd = new SqlCommand("""
        UPDATE Appointments 
        SET IdPatient = @patient, IdDoctor = @doctor, AppointmentDate = @date, 
            Status = @status, Reason = @reason, InternalNotes = @notes
        WHERE IdAppointment = @id
        """, connection);

    updateCmd.Parameters.AddWithValue("@patient", request.IdPatient);
    updateCmd.Parameters.AddWithValue("@doctor", request.IdDoctor);
    updateCmd.Parameters.AddWithValue("@date", request.AppointmentDate);
    updateCmd.Parameters.AddWithValue("@status", request.Status);
    updateCmd.Parameters.AddWithValue("@reason", request.Reason);
    updateCmd.Parameters.AddWithValue("@notes", (object?)request.InternalNotes ?? DBNull.Value);
    updateCmd.Parameters.AddWithValue("@id", idAppointment);

    await updateCmd.ExecuteNonQueryAsync();
    }
    
    public async Task DeleteAppointmentAsync(int idAppointment)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        
        await using var checkCmd = new SqlCommand("SELECT Status FROM Appointments WHERE IdAppointment = @id", connection);
        checkCmd.Parameters.AddWithValue("@id", idAppointment);

        var statusObj = await checkCmd.ExecuteScalarAsync();
        
        if (statusObj == null)
        {
            throw new KeyNotFoundException($"Wizyta o ID {idAppointment} nie istnieje.");
        }
        
        string status = (string)statusObj;
        if (status == "Completed")
        {
            throw new InvalidOperationException("Nie można usunąć wizyty, która została już zakończona.");
        }
        
        await using var deleteCmd = new SqlCommand("DELETE FROM Appointments WHERE IdAppointment = @id", connection);
        deleteCmd.Parameters.AddWithValue("@id", idAppointment);
        await deleteCmd.ExecuteNonQueryAsync();
    }
}