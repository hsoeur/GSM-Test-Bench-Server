# Simulateur GSM

Ce projet inclut un simulateur de module GSM qui vous permet de développer et tester l'API sans avoir besoin du matériel physique.

## Configuration

### Utiliser le Simulateur

Pour activer le simulateur, modifiez `appsettings.json` ou `appsettings.Development.json` :

```json
{
  "UseSimulator": true
}
```

Par défaut, le simulateur est activé en mode **Development** et désactivé en mode **Production**.

### Utiliser le Module GSM Réel

Pour utiliser le module GSM physique :

```json
{
  "UseSimulator": false,
  "Gsm": {
    "PortName": "/dev/ttyUSB0",
    "BaudRate": 115200,
    "DataBits": 8,
    "StopBits": "One",
    "Parity": "None",
    "Handshake": "None",
    "NewLine": "\n"
  }
}
```

## Options du Simulateur

Vous pouvez configurer le comportement du simulateur via la section `GsmSimulator` :

```json
{
  "GsmSimulator": {
    "CommandDelayMs": 50,   // Délai pour simuler le traitement des commandes AT
 "CallConnectDelayMs": 2000,      // Délai pour simuler la connexion d'appel
  "SimulateFailures": false,       // Activer les échecs aléatoires pour tester la robustesse
    "FailurePercentage": 10,     // % de chance d'échec d'initialisation
    "CallFailurePercentage": 15,   // % de chance d'échec de connexion d'appel
    "PingFailurePercentage": 5       // % de chance d'échec de ping
  }
}
```

### Mode Test avec Échecs Simulés

Pour tester la gestion d'erreurs de votre application :

```json
{
  "UseSimulator": true,
  "GsmSimulator": {
"SimulateFailures": true,
    "FailurePercentage": 20,
    "CallFailurePercentage": 30
  }
}
```

## Utilisation

### Démarrage

```bash
dotnet run --project Api.TestGsm
```

L'API démarre sur http://localhost:5001

### Endpoints Disponibles

1. **Health Check**
   ```bash
   curl http://localhost:5001/api/gsm/health
   ```

2. **Initialisation**
   ```bash
   curl -X POST http://localhost:5001/api/gsm/init
   ```

3. **Lancer un Appel**
   ```bash
   curl -X POST http://localhost:5001/api/gsm/call \
     -H "Content-Type: application/json" \
     -d '{"phoneNumber": "+33612345678", "audioOk": true}'
   ```

4. **Raccrocher**
   ```bash
 curl -X POST http://localhost:5001/api/gsm/hangup
   ```

### Interface Swagger

Accédez à http://localhost:5001/swagger pour une interface interactive.

## Déploiement sur Ubuntu

### Avec le Simulateur (pour tests)

```bash
# Publier l'application
dotnet publish -c Release -o /opt/api-testgsm

# Créer le fichier appsettings.Production.json
cat > /opt/api-testgsm/appsettings.Production.json << 'EOF'
{
  "UseSimulator": true,
  "GsmSimulator": {
    "CommandDelayMs": 50,
    "CallConnectDelayMs": 2000
  }
}
EOF

# Créer le service systemd
sudo nano /etc/systemd/system/api-testgsm.service
```

### Avec le Module GSM Réel

```bash
# Publier l'application
dotnet publish -c Release -o /opt/api-testgsm

# Créer le fichier appsettings.Production.json
cat > /opt/api-testgsm/appsettings.Production.json << 'EOF'
{
  "UseSimulator": false,
  "Gsm": {
    "PortName": "/dev/ttyUSB0",
    "BaudRate": 115200,
    "DataBits": 8,
    "StopBits": "One",
    "Parity": "None",
    "Handshake": "None",
    "NewLine": "\n"
  }
}
EOF

# Ajouter l'utilisateur au groupe dialout pour accès au port série
sudo usermod -a -G dialout $USER

# Créer le service systemd
sudo nano /etc/systemd/system/api-testgsm.service
```

### Service Systemd

```ini
[Unit]
Description=API TestGsm (.NET 8)
After=network.target

[Service]
WorkingDirectory=/opt/api-testgsm
ExecStart=/usr/bin/dotnet /opt/api-testgsm/Api.TestGsm.dll
Restart=always
RestartSec=5
User=apiuser
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

[Install]
WantedBy=multi-user.target
```

Activer et démarrer le service :

```bash
sudo systemctl daemon-reload
sudo systemctl enable api-testgsm
sudo systemctl start api-testgsm
sudo systemctl status api-testgsm
```

## Logs

Les logs sont stockés dans le dossier `logs/` avec rotation quotidienne et numérotation par redémarrage.

```bash
# Voir les logs en temps réel
journalctl -u api-testgsm -f

# Ou dans le dossier logs de l'application
tail -f /opt/api-testgsm/logs/$(date +%Y-%m-%d)/log-*.log
```

## Avantages du Simulateur

- ? Pas besoin de matériel physique pour développer
- ? Tests plus rapides et reproductibles
- ? Simulation d'échecs pour tester la robustesse
- ? Développement sur n'importe quelle machine (Windows, Mac, Linux)
- ? Logs détaillés pour le débogage
- ? Basculement facile entre simulateur et matériel réel

## Limitations du Simulateur

- ?? Ne simule pas les vraies réponses du modem (toujours "OK")
- ?? Ne peut pas tester les vrais appels téléphoniques
- ?? Les délais sont approximatifs

Pour les tests finaux et la production, utilisez toujours le vrai module GSM.
