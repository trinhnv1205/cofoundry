﻿using Cofoundry.Core;
using Cofoundry.Domain.CQS;
using Cofoundry.Domain.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Cofoundry.Domain.Internal
{
    /// <summary>
    /// Logs a user into the application for a specified user area
    /// using username and password credentials. Checks for valid
    /// credentials and includes additional security checking such
    /// as preventing excessive authentication attempts. Validation errors
    /// are thrown as ValidationExceptions.
    /// </summary>
    public class SignInUserWithCredentialsCommandHandler
        : ICommandHandler<SignInUserWithCredentialsCommand>
        , IIgnorePermissionCheckHandler
    {
        private readonly ILogger<SignInUserWithCredentialsCommandHandler> _logger;
        private readonly CofoundryDbContext _dbContext;
        private readonly IDomainRepository _domainRepository;
        private readonly IUserSignInService _signInService;
        private readonly IPasswordUpdateCommandHelper _passwordUpdateCommandHelper;
        private readonly IUserAreaDefinitionRepository _userAreaDefinitionRepository;

        public SignInUserWithCredentialsCommandHandler(
            ILogger<SignInUserWithCredentialsCommandHandler> logger,
            CofoundryDbContext dbContext,
            IDomainRepository domainRepository,
            IUserSignInService signInService,
            IPasswordUpdateCommandHelper passwordUpdateCommandHelper,
            IUserAreaDefinitionRepository userAreaDefinitionRepository
            )
        {
            _logger = logger;
            _dbContext = dbContext;
            _domainRepository = domainRepository;
            _signInService = signInService;
            _passwordUpdateCommandHelper = passwordUpdateCommandHelper;
            _userAreaDefinitionRepository = userAreaDefinitionRepository;
        }


        public async Task ExecuteAsync(SignInUserWithCredentialsCommand command, IExecutionContext executionContext)
        {
            var authResult = await GetUserSignInInfoAsync(command, executionContext);
            authResult.ThrowIfNotSuccess();

            if (authResult.User.RequirePasswordChange)
            {
                // Even if a password change is required, we should take the oportunity to rehash
                if (authResult.User.PasswordRehashNeeded)
                {
                    await RehashPassword(authResult.User.UserId, command.Password);
                }

                UserValidationErrors.Authentication.PasswordChangeRequired.Throw();
            }

            if (!authResult.User.IsAccountVerified)
            {
                var options = _userAreaDefinitionRepository.GetOptionsByCode(command.UserAreaCode);

                if (options.AccountVerification.RequireVerification)
                {
                    UserValidationErrors.Authentication.AccountNotVerified.Throw();
                }
            }

            // Successful credentials auth invalidates any account recovery requests
            await _domainRepository
                .WithContext(executionContext)
                .ExecuteCommandAsync(new InvalidateAuthorizedTaskBatchCommand(authResult.User.UserId, UserAccountRecoveryAuthorizedTaskType.Code));

            await _signInService.SignInAuthenticatedUserAsync(
                command.UserAreaCode,
                authResult.User.UserId,
                command.RememberUser
                );
        }

        private Task<UserCredentialsValidationResult> GetUserSignInInfoAsync(SignInUserWithCredentialsCommand command, IExecutionContext executionContext)
        {
            var query = new ValidateUserCredentialsQuery()
            {
                UserAreaCode = command.UserAreaCode,
                Username = command.Username,
                Password = command.Password,
                PropertyToValidate = nameof(command.Password)
            };

            return _domainRepository
                .WithContext(executionContext)
                .ExecuteQueryAsync(query);
        }

        /// <remarks>
        /// So far this is only used here, but it could be separated into it's own
        /// command if it was used elsewhere. 
        /// </remarks>
        private async Task RehashPassword(int userId, string password)
        {
            var user = await _dbContext
                .Users
                .SingleOrDefaultAsync(u => u.UserId == userId);

            EntityNotFoundException.ThrowIfNull(user, userId);

            _logger.LogDebug("Rehashing password for user {UserId}", user.UserId);
            _passwordUpdateCommandHelper.UpdatePasswordHash(password, user);

            await _dbContext.SaveChangesAsync();
        }
    }
}