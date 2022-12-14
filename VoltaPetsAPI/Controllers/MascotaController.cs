using CloudinaryDotNet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using VoltaPetsAPI.Data;
using VoltaPetsAPI.Helpers;
using VoltaPetsAPI.Models;
using VoltaPetsAPI.Models.ViewModels;

namespace VoltaPetsAPI.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class MascotaController : ControllerBase
    {
        private readonly VoltaPetsContext _context;
        private readonly Cloudinary _cloudinary;

        public MascotaController(VoltaPetsContext context, Cloudinary cloudinary)
        {
            _context = context;
            _cloudinary = cloudinary;
        }


        [HttpPost]
        [Route("Registrar")]
        [Authorize(Policy = "Tutor")]
        public async Task<IActionResult> RegistrarMascota(MascotaVM mascotaVM)
        {
            if (!ModelState.IsValid)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                return BadRequest(ModelState);
            }

            //validar fecha nacimiento o adopcion mascota
            DateTime fechaMaximaPermitida = DateTime.Now - TimeSpan.FromDays(180);

            //validar que la fecha de nacimiento cumpla con 6 meses de edad o más
            if (mascotaVM.IsFechaNacimiento && mascotaVM.FechaNacimiento >= fechaMaximaPermitida)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                return BadRequest(new { mensaje = "No se permiten mascotas menores a 6 meses de edad" });
            }

            //validar que la fecha de nacimiento o adopción no implique una edad mayor a 30 años
            DateTime fechaMinimaPermitida = DateTime.Now - TimeSpan.FromDays(10950);

            if (mascotaVM.FechaNacimiento <= fechaMinimaPermitida)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                return BadRequest(new { mensaje = "Ingresa una fecha de nacimiento o adopción real (No es posible que un perro tenga más de 30 años)" });
            }

            //validar edad mascota
            var tiempoMascota = CalcularAnios(mascotaVM.FechaNacimiento);

            if (mascotaVM.IsFechaNacimiento && mascotaVM.EdadRegistro > 0 && mascotaVM.EdadRegistro != tiempoMascota)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                return BadRequest(new { mensaje = "La edad ingresada no coincide con la edad calculada desde la fecha de nacimiento (La edad no es obligatoria si ingresa una fecha de nacimiento) " });
            }

            if (!mascotaVM.IsFechaNacimiento && mascotaVM.EdadRegistro == 0)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                return BadRequest(new { mensaje = "La fecha de adopcion requiere que ingrese la edad de la mascota para poder actualizarla automáticamente" });
            }

            if (!mascotaVM.IsFechaNacimiento && mascotaVM.EdadRegistro < tiempoMascota)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                return BadRequest(new { mensaje = $"La edad ingresada no puede ser menor a {tiempoMascota} años, correspondiente al tiempo de adopcion" });
            }

            if (mascotaVM.IsFechaNacimiento)
            {
                mascotaVM.EdadRegistro = null;
            }

            //obtener grupo etario
            GrupoEtario grupoEtario;

            if (mascotaVM.IsFechaNacimiento)
            {
                grupoEtario = await _context.GrupoEtarios.FirstOrDefaultAsync(gpe => gpe.EdadInferior <= tiempoMascota && gpe.EdadSuperior > tiempoMascota);

                if (grupoEtario == null)
                {
                    ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                    return NotFound(new { mensaje = "No se pudo obtener el grupo etario de la mascota" });
                }

            }
            else
            {
                grupoEtario = await _context.GrupoEtarios.FirstOrDefaultAsync(gpe => gpe.EdadInferior <= (double)mascotaVM.EdadRegistro && gpe.EdadSuperior > (double)mascotaVM.EdadRegistro);

                if (grupoEtario == null)
                {
                    ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                    return NotFound(new { mensaje = "No se pudo obtener el grupo etario de la mascota" });
                }

            }


            //Obtener usuario logeado
            var claims = (ClaimsIdentity)User.Identity;
            var codUser = claims.FindFirst(JwtRegisteredClaimNames.Sid).Value;
            int codigoUsuario;

            if (int.TryParse(codUser, out int id))
            {
                codigoUsuario = id;
            }
            else
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                return BadRequest(new { mensaje = "Error en obtener el codigo del usuario actual" });
            }

            //obtener tutor asociado al usuario
            var tutor = await _context.Tutores.FirstOrDefaultAsync(t => t.CodigoUsuario == codigoUsuario);

            if (tutor == null)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                return NotFound(new { mensaje = "No se pudo encontrar el tutor de la mascota" });
            }

            //registrar mascota
            Mascota mascota = new Mascota
            {
                Nombre = mascotaVM.Nombre,
                Descripcion = mascotaVM.Descripcion,
                Esterilizado = mascotaVM.Esterilizado,
                FechaNacimiento = mascotaVM.FechaNacimiento,
                EdadRegistro = mascotaVM.EdadRegistro,
                CodigoTutor = tutor.Id,
                CodigoRaza = (int)mascotaVM.CodigoRaza,
                CodigoTamanio = (int)mascotaVM.CodigoTamanio,
                CodigoSexo = (int)mascotaVM.CodigoSexo,
                CodigoEtario = grupoEtario.Id,
                CodigoEstadoMascota = 1,
                Imagen = mascotaVM.imagen
            };

            //validar que la mascota no existe en la BD
            if (await _context.Mascotas.Where(m => m.Nombre == mascota.Nombre && m.Esterilizado == mascota.Esterilizado && m.FechaNacimiento == mascota.FechaNacimiento && m.EdadRegistro == mascota.EdadRegistro && m.CodigoTutor == mascota.CodigoTutor && m.CodigoRaza == mascota.CodigoRaza && m.CodigoTamanio == mascota.CodigoTamanio && m.CodigoSexo == mascota.CodigoSexo && m.CodigoEtario == mascota.CodigoEtario).AnyAsync())
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                return BadRequest(new { mensaje = "La mascota registrada ya existe" });
            }

            _context.Mascotas.Add(mascota);
            var registroMascota = await _context.SaveChangesAsync();

            //validar si no se pudo registrar la mascota
            if (registroMascota <= 0)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, mascotaVM.imagen);
                return BadRequest(new { mensaje = "No se pudo registrar la mascota" });

            }

            return Ok(new
            {
                codigoMascota = mascota.Id,
                mensaje = "Mascota registrada con éxito"
            });


        }

        [HttpGet]
        [Route("Obtener/{codigoMascota:int}")]
        [Authorize(Policy = "Tutor")]
        public async Task<IActionResult> ObtenerMascota(int codigoMascota)
        {
            var mascota = await _context.Mascotas
                .Include(m => m.Imagen)
                .Include(m => m.Sexo)
                .Include(m => m.Raza)
                .Include(m => m.Tamanio)
                .Include(m => m.GrupoEtario)
                .Where(m => m.Id == codigoMascota)
                .Select(m => new Mascota
                {
                    Nombre = m.Nombre,
                    Descripcion = m.Descripcion,
                    FechaNacimiento = m.FechaNacimiento,
                    EdadRegistro = m.EdadRegistro,
                    Esterilizado = m.Esterilizado,
                    Imagen = m.Imagen,
                    Sexo = new Sexo
                    {
                        Descripcion = m.Sexo.Descripcion
                    },
                    Raza = new Raza
                    {
                        Descripcion = m.Raza.Descripcion
                    },
                    Tamanio = new Tamanio
                    {
                        Descripcion = m.Tamanio.Descripcion
                    },
                    GrupoEtario = new GrupoEtario
                    {
                        Descripcion = m.GrupoEtario.Descripcion
                    }
                })
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (mascota == null)
            {
                return BadRequest(new { mensaje = "No se pudo obtener a la mascota" });
            }

            double edadMascota = 0.0;

            if (mascota.EdadRegistro == null)
            {
                edadMascota = CalcularAnios(mascota.FechaNacimiento);
            }
            else
            {
                edadMascota = (double)mascota.EdadRegistro;
            }

            return Ok(new
            {
                Nombre = mascota.Nombre,
                Edad = edadMascota,
                GrupoEtario = mascota.GrupoEtario,
                Raza = mascota.Raza,
                Tamanio = mascota.Tamanio,
                Sexo = mascota.Sexo,
                Esterilizado = mascota.Esterilizado,
                Descripcion = mascota.Descripcion,
                Imagen = mascota.Imagen
            });

        }

        [HttpGet]
        [Route("Obtener/MisMascotas")]
        [Authorize(Policy = "Tutor")]
        public async Task<IActionResult> ObtenerMisMascotas()
        {
            var claims = (ClaimsIdentity)User.Identity;
            int codigoUsuario = UsuarioConectado.ObtenerCodigo(claims);

            if (codigoUsuario == 0)
            {
                return BadRequest(new { mensaje = "Error en obtener el codigo del usuario actual" });
            }

            var tutor = await _context.Tutores.FirstOrDefaultAsync(t => t.CodigoUsuario == codigoUsuario);

            if (tutor == null)
            {
                return NotFound(new { mensaje = "No se pudo encontrar al tutor" });
            }

            var misMascotas = await _context.Mascotas
                .Include(m => m.EstadoMascota)
                .Where(m => m.CodigoTutor == tutor.Id)
                .Select(m => new Mascota
                {
                    Id = m.Id,
                    Nombre = m.Nombre,
                    Raza = m.Raza,
                    Imagen = m.Imagen,
                    EdadRegistro = m.EdadRegistro,
                    EstadoMascota = new EstadoMascota
                    {
                        Descripcion = m.EstadoMascota.Descripcion
                    }
                }).AsNoTracking()
                .ToListAsync();

            if (!misMascotas.Any())
            {
                return NotFound(new { mensaje = "No existen mascotas" });
            }
            else
            {
                return Ok(misMascotas);
            }

        }


        [HttpPut]
        [Route("Editar/{codigoMascota:int}")]
        [Authorize(Policy = "Tutor")]
        public async Task<IActionResult> EditarMascota(int codigoMascota, MascotaVM mascotaVM)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            //validar fecha nacimiento o adopcion mascota
            DateTime fechaMaximaPermitida = DateTime.Now - TimeSpan.FromDays(180);

            if (mascotaVM.FechaNacimiento >= fechaMaximaPermitida)
            {
                return BadRequest(new { mensaje = "No se permiten mascotas menores a 6 meses de edad" });
            }

            DateTime fechaMinimaPermitida = DateTime.Now - TimeSpan.FromDays(10950);

            if (mascotaVM.FechaNacimiento <= fechaMinimaPermitida)
            {
                return BadRequest(new { mensaje = "Ingresa una fecha de nacimiento real (No es posible que un perro tenga más de 30 años)" });
            }

            //validar edad mascota
            var tiempoMascota = CalcularAnios(mascotaVM.FechaNacimiento);

            if (mascotaVM.EdadRegistro > 0 && !mascotaVM.IsYear)
            {
                mascotaVM.EdadRegistro = ObtenerEdadAnios((double)mascotaVM.EdadRegistro);
            }

            if (mascotaVM.IsFechaNacimiento && mascotaVM.EdadRegistro > 0 && mascotaVM.EdadRegistro != tiempoMascota)
            {
                return BadRequest(new { mensaje = "La edad ingresada no coincide con la edad calculada desde la fecha de nacimiento (La edad no es obligatoria si ingresa una fecha de nacimiento) " });
            }

            if (!mascotaVM.IsFechaNacimiento && mascotaVM.EdadRegistro == 0)
            {
                return BadRequest(new { mensaje = "La fecha de adopcion requiere que ingrese la edad de la mascota para poder actualizarla automáticamente" });
            }

            if (!mascotaVM.IsFechaNacimiento && mascotaVM.EdadRegistro < tiempoMascota)
            {
                return BadRequest(new { mensaje = $"La edad ingresada no puede ser menor a {tiempoMascota} años, correspondiente al tiempo de adopcion" });
            }

            if (!mascotaVM.IsFechaNacimiento && (tiempoMascota + (double)mascotaVM.EdadRegistro) < 0.5)
            {
                return BadRequest(new { mensaje = "No se permiten mascotas menores a 6 meses de edad" });
            }

            if (mascotaVM.IsFechaNacimiento)
            {
                mascotaVM.EdadRegistro = null;
            }

            //obtener grupo etario
            GrupoEtario grupoEtario;

            if (mascotaVM.IsFechaNacimiento)
            {
                grupoEtario = await _context.GrupoEtarios.FirstOrDefaultAsync(gpe => gpe.EdadInferior <= tiempoMascota && gpe.EdadSuperior > tiempoMascota);

                if (grupoEtario == null)
                {
                    return NotFound(new { mensaje = "No se pudo obtener el grupo etario de la mascota" });
                }

            }
            else
            {
                grupoEtario = await _context.GrupoEtarios.FirstOrDefaultAsync(gpe => gpe.EdadInferior <= (double)mascotaVM.EdadRegistro && gpe.EdadSuperior > (double)mascotaVM.EdadRegistro);

                if (grupoEtario == null)
                {
                    return NotFound(new { mensaje = "No se pudo obtener el grupo etario de la mascota" });
                }

            }

            //obtener mascota a editar
            var mascota = await _context.Mascotas.FindAsync(codigoMascota);

            if (mascota == null)
            {
                return NotFound(new { mensaje = "No se pudo encontrar a la mascota" });
            }

            //registrar mascota
            mascota.Nombre = mascotaVM.Nombre;
            mascota.Descripcion = mascotaVM.Descripcion;
            mascota.Esterilizado = mascotaVM.Esterilizado;
            mascota.FechaNacimiento = mascotaVM.FechaNacimiento;
            mascota.EdadRegistro = mascotaVM.EdadRegistro;
            mascota.CodigoRaza = (int)mascotaVM.CodigoRaza;
            mascota.CodigoTamanio = (int)mascotaVM.CodigoTamanio;
            mascota.CodigoSexo = (int)mascotaVM.CodigoSexo;
            mascota.CodigoEtario = grupoEtario.Id;

            //validar que no exista la mascota con los nuevos datos en la BD
            if (await _context.Mascotas.Where(m => m.Nombre == mascota.Nombre && m.Esterilizado == mascota.Esterilizado && m.FechaNacimiento == mascota.FechaNacimiento && m.EdadRegistro == mascota.EdadRegistro && m.CodigoTutor == mascota.CodigoTutor && m.CodigoRaza == mascota.CodigoRaza && m.CodigoTamanio == mascota.CodigoTamanio && m.CodigoSexo == mascota.CodigoSexo && m.CodigoEtario == mascota.CodigoEtario).AnyAsync())
            {
                return BadRequest(new { mensaje = "La mascota editada ya existe" });
            }

            var registroMascota = await _context.SaveChangesAsync();

            //validar si no se pudo registrar la mascota
            if (registroMascota <= 0)
            {
                return BadRequest(new { mensaje = "No se pudo editar la mascota" });

            }

            return Ok(new
            {
                mensaje = "Mascota editada con éxito"
            });
        }

        [HttpPut]
        [Route("CambiarImagen/{codigoMascota:int}")]
        [Authorize(Policy = "Tutor")]
        public async Task<IActionResult> CambiarImagenMascota(int codigoMascota, ImagenVM imagenVM)
        {
            if (!ModelState.IsValid)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, imagenVM.ToImagen());
                return BadRequest(ModelState);
            }

            var mascota = await _context.Mascotas
                .Include(m => m.Imagen)
                .FirstOrDefaultAsync(m => m.Id == codigoMascota);

            if (mascota == null)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, imagenVM.ToImagen());
                return NotFound(new { mensaje = "No se pudo obtener a la mascota" });
            }

            string imagenAnteriorPublicId = mascota.Imagen.Public_Id;

            mascota.Imagen.Public_Id = imagenVM.Public_Id;
            mascota.Imagen.Url = imagenVM.Url;
            mascota.Imagen.Path = imagenVM.Path;

            var modificacionImagen = await _context.SaveChangesAsync();

            if (modificacionImagen <= 0)
            {
                ImagenCloudinary.EliminarImagenHosting(_cloudinary, imagenVM.ToImagen());
                return BadRequest(new { mensaje = "No se pudo cambiar la imagen de la mascota" });

            }

            imagenVM.Public_Id = imagenAnteriorPublicId;
            ImagenCloudinary.EliminarImagenHosting(_cloudinary, imagenVM.ToImagen());

            return NoContent();

        }


        private double ObtenerEdadAnios(double edadMes)
        {
            var edadAnios = edadMes / 12;

            if (edadAnios < 1)
            {
                return Math.Round(edadAnios, 2, MidpointRounding.ToZero);
            }
            else
            {
                return Math.Truncate(edadAnios);
            }
        }

        private double CalcularAnios(DateTime fechaNacimiento)
        {
            TimeSpan diferenciaTiempo = DateTime.Now.Date - fechaNacimiento;

            double anios = diferenciaTiempo.TotalDays / 365.2425;

            if (anios < 1)
            {
                return Math.Round(anios, 2, MidpointRounding.ToZero);
            }
            else
            {
                return Math.Truncate(anios);
            }

        }

        /*
        
        private int CalcularEdad(DateTime fechaNacimiento)
        {
            //Obtener diferencias entre la fecha actual y la fecha de nacimiento
            int diferenciaYear = DateTime.Now.Year - fechaNacimiento.Year;
            int diferenciaMes = DateTime.Now.Month - fechaNacimiento.Month;
            int diferenciaDia = DateTime.Now.Day - fechaNacimiento.Day;

            //Si la diferencia de mes es negativa significa que el mes actual es anterior al mes de nacimiento
            if (diferenciaMes < 0)
            {
                //Como aun no se ha alcanzado el mes de nacimiento, la edad es la diferencia de anios menos uno
                return diferenciaYear - 1;
            }

            //Si la diferencia de mes es igual a cero significa que el mes actual es el mes de nacimiento
            if (diferenciaMes == 0)
            {
                //Si la diferencia de dias es negativa significa que el dia actual es anterior al dia de nacimiento (No se ha cumplido anios)
                return diferenciaDia < 0 ? diferenciaYear - 1 : diferenciaYear;
            }

            //Si no se cumplen las condiciones anteriores, el mes es mayor a cero, por lo que el mes actual ha pasado al mes de nacimiento, es decir, se cumplieron anios
            return diferenciaYear;

        }

        */
    }
}