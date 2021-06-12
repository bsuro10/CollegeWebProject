﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using DGN.Data;
using DGN.Models;
using System.Text.RegularExpressions;
using System.Net;
using System.Collections.Generic;
using System.Security.Claims;
using System;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DGN.Controllers
{
    public class UsersController : Controller
    {
        private readonly DGNContext _context;

        public UsersController(DGNContext context)
        {
            _context = context;
        }

        // GET: Users
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            return View(await _context.User.ToListAsync());
        }

        // GET: Users/Profile/5
        [Authorize]
        public async Task<IActionResult> Profile(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.User
                .FirstOrDefaultAsync(m => m.Id == id);
            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(int? id, [Bind("Id,Email,Username,Firstname,Lastname,Birthday,Role,ImageLocation,About")] User user)
        {
            // TODO: If role is admin allow edit for not admin 
            // If role is not admin only edit itself and not edit role.
            if (id != user.Id)
            {
                return NotFound();
            }
            
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(user);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(user.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // GET: Users/Login
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string Email, string password)
        {            
            if (!EmailExists(Email))
            {
                ViewData["Error"] = "The email or password is incorrect";
                Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            }
            else {
                User usr = await _context.User.Include(u => u.Password).FirstOrDefaultAsync(u => u.Email == Email);
                if (usr.Password.Check(password))
                {
                    List<Claim> claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, usr.Id.ToString()),
                        new Claim(ClaimTypes.Name, usr.Username),
                        new Claim(ClaimTypes.Email, usr.Email),
                        new Claim(ClaimTypes.Role, usr.Role.ToString())
                    };

                    ClaimsIdentity claimIdentity = new ClaimsIdentity(claims, "Login");
                    AuthenticationProperties authProperties = new AuthenticationProperties
                    {
                        ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10)
                    };

                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(claimIdentity),
                        authProperties);
                    return Redirect("/");
                }
                else 
                {
                    ViewData["Error"] = "The email or password is incorrect";
                    Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                }
            }
            return View();
        }


        // GET: Users/Register
        public IActionResult Register()
        {
            return View();
        }

        // POST: Users/Registar
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register([Bind("Id,Email,Username,Firstname,Lastname,Birthday,Role,ImageLocation,About")] User user, string plainPass, string confirmPass)
        {
            if (UsernameExists(user.Username))
            {
                ModelState.AddModelError("Username", "Username is aleardy exists");
                Response.StatusCode = (int)HttpStatusCode.Conflict;
            }
            if (EmailExists(user.Email))
            {
                ModelState.AddModelError("Email", "This Email address is aleady in use");
                Response.StatusCode = (int)HttpStatusCode.Conflict;
            }
            if (CanUsePassword(plainPass, confirmPass) && ModelState.IsValid)
            {
                user.Password = new Password(user.Id, plainPass, user);
                _context.Add(user);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(user);
        }

        // GET: Users/ChangePassword/5
        [Authorize]
        public async Task<IActionResult> ChangePassword(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }
            string userRole = HttpContext.User.FindFirst(ClaimTypes.Role)?.Value;
            string userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // Only when the user tring to change his own password
            if (!userId.Equals(id.ToString()))
            {
                if (!userRole.Equals(UserRole.Admin.ToString()))
                {
                    Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    return Unauthorized();
                }
            }

            User user = await _context.User.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // POST: Users/ChangePassword/5
        // Leting users to change their own passwords
        // And admins change all users passwords
        //
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> ChangePassword(int? id, string currentPassword, string newPassword, string confirmNewPassword)
        {
            
            if (id == null)
            {
                return NotFound();
            }

            string userRole = HttpContext.User.FindFirst(ClaimTypes.Role)?.Value;
            string userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            User user = await _context.User.Include(u => u.Password).FirstOrDefaultAsync(u => u.Id == id);

            if (!userId.Equals(id.ToString()))
            {
                if (!userRole.Equals(UserRole.Admin.ToString()))
                {
                    Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    return Unauthorized();
                }
            }
            else 
            {
                if (currentPassword == null) 
                {
                    ViewData["Error"] = "You must specify current password";
                    return View();
                }
                if (!user.Password.Check(currentPassword)) 
                {
                    ViewData["Error"] = "Password is not correct";
                    return View();
                }
            }

            if (CanUsePassword(newPassword, confirmNewPassword)) {
                // Creating the new password
                user.Password = new Password(user.Id, newPassword, user);
                _context.User.Update(user);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View();
        }

        // GET: Users/Delete/5
        [Authorize]
        public async Task<IActionResult> Delete(int? id)
        {
            string userRole = HttpContext.User.FindFirst(ClaimTypes.Role)?.Value;
            string userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (id == null)
            {
                return NotFound();
            }
            if (!userId.Equals(id.ToString()) && !userRole.Equals(UserRole.Admin.ToString())) 
            {
                return Unauthorized();
            }

            User user = await _context.User
                .FirstOrDefaultAsync(m => m.Id == id);
            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // POST: Users/Delete/5
        // Allowing delete when
        // 1. user deletes itself
        // 2. Admin delete not admin user
        // in both cases the user needs to confirm its password
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> DeleteConfirmed(int id, string plainPass)
        {
            
            string userRole = HttpContext.User.FindFirst(ClaimTypes.Role)?.Value;
            string userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            bool logout = false;

            User user = await _context.User.Include(u => u.Password).FirstOrDefaultAsync(u => u.Id == id);

            if (plainPass == null)
            {
                ViewData["Error"] = "Your password is required";
                return View(user);
            }

            if (!userId.Equals(id.ToString()))
            {
                if (userRole.Equals(UserRole.Admin.ToString()))
                {
                    User currentUser = await _context.User.Include(u => u.Password).FirstOrDefaultAsync(u => u.Id.ToString() == userId);
                    if (!currentUser.Password.Check(plainPass))
                    {
                        ViewData["Error"] = "Password is not correct";
                        return View(user);
                    }
                    if (user.Role == UserRole.Admin) 
                    {
                        ViewData["Error"] = "Can not delete admin user";
                        return View(user);
                    }
                }
                else
                {
                    Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    return Unauthorized();
                }
            }
            else 
            {
                if (!user.Password.Check(plainPass))
                {
                    ViewData["Error"] = "password is not correct";
                    return View(user);
                }
                logout = true;
            }

            _context.User.Remove(user);
            await _context.SaveChangesAsync();
            if (logout) 
            {
                await Logout();
            }
            return Redirect("/");
        }
        
        private bool UserExists(int id)
        {
            return _context.User.Any(e => e.Id == id);
        }

        private bool EmailExists(string Email)
        {
            return _context.User.Any(e => e.Email == Email);
        }

        private bool UsernameExists(string Username)
        {
            return _context.User.Any(e => e.Username == Username);
        }
        private bool ValidPass(string plainPass)
        {
            Regex regex = new Regex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$");
            return regex.IsMatch(plainPass);
        }

        private bool CanUsePassword(string password, string confirmPassword) {
            // Making sure new password is valid
            if (!ValidPass(password))
            {
                ViewData["Error"] = "The minumum requierments are: 8 characters long containing 1 uppercase letter, 1 lowercase letter, a number and a special character";
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return false;
            }
            // Making sure new password confirmed
            if (!password.Equals(confirmPassword))
            {
                ViewData["Error"] = "Passwords does not match";
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return false;
            }
            return true;
        }
    }
}
