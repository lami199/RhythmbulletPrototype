var routeArg = args.Length > 0 ? args[0] : null;
using var game = new RhythmbulletPrototype.Game1(routeArg);
game.Run();
