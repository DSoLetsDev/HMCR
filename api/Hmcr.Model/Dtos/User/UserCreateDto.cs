﻿using System;
using System.Collections.Generic;

namespace Hmcr.Model.Dtos.User
{
    public class UserCreateDto : IUserSaveDto
    {
        public UserCreateDto()
        {
            ServiceAreaNumbers = new List<decimal>();
            UserRoleIds = new List<decimal>();
            UserDirectory = "";
        }

        public string UserType { get; set; }
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public DateTime? EndDate { get; set; }
        public string UserDirectory { get; set; }

        public IList<decimal> ServiceAreaNumbers { get; set; }
        public IList<decimal> UserRoleIds { get; set; }
    }
}
