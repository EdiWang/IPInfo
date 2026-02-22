# Update IP Database for Azure File Share

This project provides a Docker image to update the IP database for Azure File Share. 

## Run locally

```
docker run --rm -v ./data:/data ediwang/ipinfo-updater
```

This command will run the Docker container and mount the local `./data` directory to the container's `/data` directory. The updated IP database will be saved in the `./data` directory on your local machine.