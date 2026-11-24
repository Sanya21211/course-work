using System.ComponentModel.DataAnnotations;
using ServiceCenter.Web.Domain;
using ServiceCenter.Web.Services;

namespace ServiceCenter.Web.Models;

public class LoginVm
{
    [Required(ErrorMessage = "Вкажіть логін.")]
    [Display(Name = "Логін")]
    public string Login { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть пароль.")]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; } = string.Empty;
}

public class RegisterVm
{
    [Required(ErrorMessage = "Вкажіть логін.")]
    [StringLength(50, ErrorMessage = "Логін — до 50 символів.")]
    [Display(Name = "Логін")]
    public string Login { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть ім'я.")]
    [StringLength(100, ErrorMessage = "Ім'я — до 100 символів.")]
    [Display(Name = "Ім'я")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть пароль.")]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль (не менше 15 символів)")]
    public string Password { get; set; } = string.Empty;
}

public class CreateRequestVm
{
    [Required(ErrorMessage = "Вкажіть назву обладнання.")]
    [StringLength(100, ErrorMessage = "Назва обладнання — до 100 символів.")]
    [Display(Name = "Назва обладнання")]
    public string EquipmentName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть серійний номер.")]
    [StringLength(50, ErrorMessage = "Серійний номер — до 50 символів.")]
    [Display(Name = "Серійний номер")]
    public string SerialNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Опишіть несправність.")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Опис несправності: від 10 до 2000 символів.")]
    [Display(Name = "Опис несправності")]
    public string FaultDescription { get; set; } = string.Empty;
}

public class WorkRowVm
{
    [Display(Name = "Назва роботи")]
    public string? Title { get; set; }

    [Display(Name = "Вартість, грн")]
    public decimal? Cost { get; set; }
}

public class DiagnoseVm
{
    public int Id { get; set; }
    public int Version { get; set; }
    public string Number { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть результат діагностики.")]
    [StringLength(2000, ErrorMessage = "Результат діагностики — до 2000 символів.")]
    [Display(Name = "Результат діагностики")]
    public string Diagnosis { get; set; } = string.Empty;

    public List<WorkRowVm> Works { get; set; } = Enumerable.Range(0, 5).Select(_ => new WorkRowVm()).ToList();
}

public class RequestListVm
{
    public RequestListVm(PagedResult<RequestListItem> result, string? search, RequestStatus? status)
    {
        Result = result;
        Search = search;
        Status = status;
    }

    public PagedResult<RequestListItem> Result { get; }
    public string? Search { get; }
    public RequestStatus? Status { get; }
}

public class RequestDetailsVm
{
    public RequestDetailsVm(RequestDetails details, UserRole role, IReadOnlyList<TechnicianItem> technicians)
    {
        Details = details;
        Role = role;
        Technicians = technicians;
    }

    public RequestDetails Details { get; }
    public UserRole Role { get; }
    public IReadOnlyList<TechnicianItem> Technicians { get; }

    public bool CanAssign => Role == UserRole.Receptionist
        && Details.Status is RequestStatus.New or RequestStatus.Assigned;
    public bool CanDiagnose => Role == UserRole.Technician && Details.Status == RequestStatus.Assigned;
    public bool CanDecide => Role == UserRole.Client && Details.Status == RequestStatus.Diagnosed;
    public bool CanWork => Role == UserRole.Technician && Details.Status == RequestStatus.InProgress;
    public bool CanCancel =>
        (Role == UserRole.Client && Details.Status is RequestStatus.New or RequestStatus.Diagnosed)
        || (Role == UserRole.Receptionist
            && Details.Status is RequestStatus.New or RequestStatus.Assigned or RequestStatus.Diagnosed);
}
