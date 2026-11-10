using System;

namespace Mixed.Core;

public interface IClock { DateTime Now(); }

public record Order(int Id);
