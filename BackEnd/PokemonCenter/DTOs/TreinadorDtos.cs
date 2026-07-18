using System.ComponentModel.DataAnnotations;

namespace PokemonCenter.DTOs;

public record CriarTreinadorRequest(
        [property: Required] string Nome,
        [property: Required, EmailAddress] string Email,
        [property: Required] string CidadeDeOrigem);

public record TreinadorResponse(int Id, string Nome, string Email, string CidadeDeOrigem);
