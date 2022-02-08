﻿using Cofoundry.Domain.CQS;
using System.ComponentModel.DataAnnotations;

namespace Cofoundry.Domain
{
    public class HasExceededMaxAuthenticationAttemptsQuery : IQuery<bool>
    {
        /// <summary>
        /// The <see cref="IUserAreaDefinition.UserAreaCode"/> of the user area 
        /// being authenticated.
        /// </summary>
        [Required]
        [StringLength(3)]
        public string UserAreaCode { get; set; }

        /// <summary>
        /// The username to query. This is expected to be in a "uniquified" 
        /// format, as this should have been already processed whenever this
        /// needs to be called.
        /// </summary>
        [Required]
        public string Username { get; set; }
    }
}