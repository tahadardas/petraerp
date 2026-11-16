namespace PetraERP.Application.Security;

public interface IAdministratorConfirmation
{
    Task ConfirmAsync(Guid actorUserId, string password, CancellationToken cancellationToken);
}
