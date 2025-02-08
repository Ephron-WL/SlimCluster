# run with tty console, interactive mode, and remove container after program ends
kubectl apply -f service.yaml
kubectl apply -f configmap.yaml
kubectl apply -f persistent-volume-claim.yaml
kubectl apply -f deployment.yaml
