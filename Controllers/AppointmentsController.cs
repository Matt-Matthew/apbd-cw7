using apbd_cw7_task.DTOs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;

namespace apbd_cw7_task.Controllers;
using Microsoft.AspNetCore.Mvc;

[Route("api/[controller]")]
[ApiController]
public class AppointmentsController : ControllerBase
{
    private readonly string _connectionString;

    public AppointmentsController(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection") 
                            ?? throw new Exception("No connection string found");
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments([FromQuery] string? status,
        [FromQuery] string? patientLastName
    )
    {
        var sql = @"SELECT
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
        ORDER BY a.AppointmentDate;";

        var appointments = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql,connection);

        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@PatientLastName", (object?)patientLastName ?? DBNull.Value);
 
        try
        {
            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                appointments.Add(new AppointmentListDto
                {
                    IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                    AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    Reason = reader.GetString(reader.GetOrdinal("Reason")),
                    PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                    PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
                });
            }
        }
        catch (SqlException ex)
        {
            return StatusCode(500, new ErrorResponseDto { Message = $"Blad danych : {ex.Message}" });
        }

        return Ok(appointments);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetAppointmentDetails(int id)
    {
        var sql = @"
        SELECT 
            a.IdAppointment, a.AppointmentDate, a.Status, a.Reason, a.InternalNotes, a.CreatedAt ,
            p.FirstName + ' ' + p.LastName AS PatientFullName, p.Email AS PatientEmail, p.PhoneNumber AS PatientPhone,
            d.FirstName + ' ' + d.LastName AS DoctorFullName, d.LicenseNumber AS DoctorLicenseNumber
        FROM dbo.Appointments a
        JOIN dbo.Patients p ON a.IdPatient = p.IdPatient
        JOIN dbo.Doctors d ON a.IdDoctor = d.IdDoctor
        WHERE a.IdAppointment = @Id";

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        
        command.Parameters.AddWithValue("@Id", id);
        
        await connection.OpenAsync();
        
        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            var details = new AppointmentDetailsDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("InternalNotes")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
                PatientPhone = reader.GetString(reader.GetOrdinal("PatientPhone")),
                DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
                DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
            }; 
            return Ok(details);
        }
        return NotFound(new ErrorResponseDto { Message = $"Nie znaleziono wizyty o id {id}" });        
    }

    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto appointment)
    {
        var sql = @"
            SELECT 
            (SELECT COUNT(*) FROM dbo.Patients WHERE IdPatient = @IdP AND isActive = 1) AS PatientExists,
            (SELECT COUNT(*) FROM dbo.Doctors WHERE IdDoctor = @IdD AND isActive = 1) AS DoctorExists,
            (SELECT COUNT(*) FROM dbo.Appointments WHERE IdDoctor = @IdD AND AppointmentDate = @Date) AS DoctorBusy";

        if (appointment.AppointmentDate < DateTime.Now)
        {
            return BadRequest(new ErrorResponseDto { Message = "Termin nie może być w przeszłości" });
        }
        
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(sql, connection);
        
        cmd.Parameters.AddWithValue("@IdP", appointment.IdPatient);
        cmd.Parameters.AddWithValue("@IdD", appointment.IdDoctor);
        cmd.Parameters.AddWithValue("@Date", appointment.AppointmentDate);
        
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        if (reader.GetInt32(0) == 0) 
            return BadRequest(new ErrorResponseDto { Message = "Pacjent o podanym ID nie istnieje lub jest nieaktywny" });
        if (reader.GetInt32(1) == 0) 
            return BadRequest(new ErrorResponseDto { Message = "Lekarz o podanym ID nie istnieje lub jest nieaktywny" });
        if(reader.GetInt32(2) > 0 ) 
            return Conflict(new ErrorResponseDto { Message = "Ten lekarz ma juz umuwiona wizyte" });

        var inserter = @"
        INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
        VALUES (@IdP, @IdD, @Date, 'Scheduled', @Reason);
        SELECT SCOPE_IDENTITY();";
        
        await reader.CloseAsync();
        
        await using var insert = new SqlCommand(inserter,connection);
        insert.Parameters.AddWithValue("@IdP",appointment.IdPatient);
        insert.Parameters.AddWithValue("@IdD",appointment.IdDoctor);
        insert.Parameters.AddWithValue("@Date",appointment.AppointmentDate);
        insert.Parameters.AddWithValue("@Reason", appointment.Reason);
        
        var newId = Convert.ToInt32(await insert.ExecuteScalarAsync());
        
        return Created($"api/Appointments/{newId}", newId);
        
    }
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAppointment(int id, [FromBody] UpdateAppointmentRequestDto dto)
    {
        var allowedStatus = new HashSet<string>() { "Scheduled", "Completed", "Cancelled" };

        if (!allowedStatus.Contains(dto.Status))
        {
            return BadRequest(new ErrorResponseDto { Message = "Nieprawidlowy Status" });
        }
        
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var getDataSQL = @"SELECT Status,AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @Id ";
        await using var cmd = new SqlCommand(getDataSQL, connection);
        cmd.Parameters.AddWithValue("@Id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) 
            return NotFound(new ErrorResponseDto { Message = $"Wizyta o {id} nie istnieje" });

        var currentBdStatus = reader.GetString(0);
        var currentDate = reader.GetDateTime(1);
        
        await reader.CloseAsync();

        if (currentBdStatus == "Completed" && currentDate != dto.AppointmentDate)
        {
            return BadRequest(new ErrorResponseDto { Message = "Nie mozna zmienic daty jak jest Completed status" });
        }

        var sql = @"SELECT
                    (SELECT COUNT(*) FROM dbo.Patients WHERE IdPatient = @IdP AND isActive = 1) AS PatientExists,
                    (SELECT COUNT(*) FROM dbo.Doctors WHERE IdDoctor = @IdD AND isActive = 1) AS DoctorExists,
                    (SELECT COUNT(*) FROM dbo.Appointments
                    WHERE IdDoctor = @IdD
                        AND AppointmentDate = @Date
                        AND IdAppointment != @Id) AS DoctorBusy";
        
        await using var valCmd = new SqlCommand(sql,connection);
        valCmd.Parameters.AddWithValue("@IdP",dto.IdPatient);
        valCmd.Parameters.AddWithValue("@IdD",dto.IdDoctor);
        valCmd.Parameters.AddWithValue("@Date", dto.AppointmentDate);
        valCmd.Parameters.AddWithValue("@Id", id);

        await using var valReader = await valCmd.ExecuteReaderAsync();
        await valReader.ReadAsync();
        
        if(valReader.GetInt32(0) == 0) 
            return BadRequest(new ErrorResponseDto { Message = "Wskazany patient nie istnieje lub jest nieaktywny" });
        if (valReader.GetInt32(1) == 0) 
            return BadRequest(new ErrorResponseDto { Message = "Podany Doktor nie istnieje lub jest nieaktywny" });
        if (valReader.GetInt32(2) > 0) 
            return Conflict(new ErrorResponseDto { Message = "Doctor ma juz inna wizyte" });
        
        await valReader.CloseAsync();
        
        var updateSql = @"UPDATE dbo.Appointments SET IdPatient = @IdP, IdDoctor = @IdDoctor, AppointmentDate = @Date, Status = @Status, Reason = @Reason, InternalNotes = @Notes WHERE IdAppointment = @Id";
        
        await using var updateCmd = new SqlCommand(updateSql, connection);
        updateCmd.Parameters.AddWithValue("@IdP", dto.IdPatient);
        updateCmd.Parameters.AddWithValue("@IdDoctor", dto.IdDoctor);
        updateCmd.Parameters.AddWithValue("@Date", dto.AppointmentDate);
        updateCmd.Parameters.AddWithValue("@Status", dto.Status);
        updateCmd.Parameters.AddWithValue("@Reason", dto.Reason);
        
        updateCmd.Parameters.AddWithValue("@Notes", (object?)dto.InternalNotes ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("@Id", id);
        
        await updateCmd.ExecuteNonQueryAsync();
        
        return NoContent();
        
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAppointment(int id )
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var sql = @"SELECT Status FROM dbo.Appointments WHERE IdAppointment = @Id";
        await using var cmd = new SqlCommand(sql,connection);
        cmd.Parameters.AddWithValue("@Id", id);

        var status = await cmd.ExecuteScalarAsync();

        if (status == null)
        {
            return NotFound(new ErrorResponseDto { Message = $"Wizyta od {id} nie istnieje" });
        }

        var statusReq = status.ToString();

        if (statusReq!.Equals("Completed"))
        {
            return Conflict(new ErrorResponseDto { Message = "Nie mozna usunac wizyty o statusie Completed" });
        }
        
        var deleteSQL = @"DELETE FROM dbo.Appointments WHERE IdAppointment = @Id";
        await using var deleteCmd = new SqlCommand(deleteSQL,connection);
        deleteCmd.Parameters.AddWithValue("@Id", id);

        await deleteCmd.ExecuteNonQueryAsync();
        return NoContent();
    }

}