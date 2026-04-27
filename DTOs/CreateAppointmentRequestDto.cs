using System.ComponentModel.DataAnnotations;

namespace apbd_cw7_task.DTOs;

public class CreateAppointmentRequestDto 
{
   
   public int IdPatient { get; set; }
   
   public int IdDoctor { get; set; }
   
   public DateTime AppointmentDate  { get; set; }
   
   [Required(ErrorMessage = "Opis jest wymagany")]
   [MaxLength(250, ErrorMessage = "Opis musi byc < 250 znakow")]
   public string Reason { get; set; } = string.Empty;
   
}