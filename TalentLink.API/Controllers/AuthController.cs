﻿using Microsoft.AspNetCore.Mvc;
using TalentLink.Application.DTOs;
using TalentLink.Application.Interfaces;
using TalentLink.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TalentLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;



namespace TalentLink.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly TalentLinkDbContext _context;
        private readonly IUserService _userService;
        private readonly IConfiguration _configuration;

        public AuthController(IUserService userService, IConfiguration configuration, TalentLinkDbContext context)
        {
            _context = context;
            _userService = userService;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegisterDto input)
        {
            User user = input.Role switch
            {
                0 => new Student(),
                1 => new Senior(),
                2 => new Parent(),
                3 => new Admin(),
                _ => throw new ArgumentException("Invalid role")
            };

            user.Name = input.Name;
            user.Email = input.Email;
            user.Role = (UserRole)input.Role;

            var createdUser = await _userService.RegisterAsync(user, input.Password);

            
            if (user is Parent parent && !string.IsNullOrWhiteSpace(input.StudentEmail))
            {
                var child = await _userService.FindByEmailAsync(input.StudentEmail);
                if (child is Student student)
                {
                    student.VerifiedByParentId = parent.Id;

                    
                    var verified = new VerifiedStudent
                    {
                        ParentId = parent.Id,
                        StudentId = student.Id
                    };

                    await _context.VerifiedStudents.AddAsync(verified);
                    await _context.SaveChangesAsync();
                }
            }

            return Ok();
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var user = await _userService.AuthenticateAsync(dto.Email, dto.Password);
            if (user == null)
                return Unauthorized("Invalid credentials");

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        }),
                Expires = DateTime.UtcNow.AddMinutes(double.Parse(_configuration["Jwt:ExpiresInMinutes"]!)),
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);

            // Hole VerifiedByParentId, falls Student
            Guid? verifiedByParentId = null;
            if (user is Student student)
            {
                verifiedByParentId = student.VerifiedByParentId;
            }

            return Ok(new AuthResponseDto
            {
                Token = tokenString,
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role.ToString(),
                VerifiedByParentId = verifiedByParentId
            });
        }
    }
}
