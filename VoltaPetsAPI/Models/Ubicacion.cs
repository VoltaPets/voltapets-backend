﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VoltaPetsAPI.Models
{
    [Table("ubicacion")]
    public class Ubicacion
    {
        [Column("codigo_ubicacion")]
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CodigoUbicacion { get; set; }

        [Column("direccion")]
        [Required]
        [StringLength(200)]
        public string Direccion { get; set; }

        [Column("departamento")]
        public int? Departamento { get; set; }

        [Column("latitud")]
        [Required]
        public double Latitud { get; set; }

        [Column("longitud")]
        [Required]
        public double Longitud { get; set; }

        //FK Comuna
        [Column("codigo_comuna")]
        [Required]
        public int CodigoComuna { get; set; }

        [ForeignKey("CodigoComuna")]
        public virtual Comuna Comuna { get; set; }

        //Relacion con Paseador
        public virtual ICollection<Paseador> Paseadores { get; set; }

        //Relacion con Tutor
        public virtual ICollection<Tutor> Tutores { get; set; }

    }
}