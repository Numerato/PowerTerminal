# Find the nginx container name
docker ps
 
# Test config
docker exec nginx ps nginx -t
 
# Reload (no downtime)
 docker exec nginx nginx -s reload

# Rebuild docker
docker compose up -d --build

# Docker show logs
docker logs -f nginx

