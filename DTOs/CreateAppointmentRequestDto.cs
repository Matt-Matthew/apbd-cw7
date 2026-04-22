namespace apbd_cw7_task.DTOs;

public class CreateAppointmentRequestDto
{
   public int IdPatient { get; set; }
   public int IdDoctor { get; set; }
   public DateTime AppointmentDate  { get; set; }
   public string Reason { get; set; } = string.Empty;
   
}