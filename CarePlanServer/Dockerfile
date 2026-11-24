# Cloud Run image for the care-plan server. Built by: gcloud run deploy --source CarePlanServer
FROM python:3.12-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 CARE_PLAN_STORE=firestore
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY care_plan_server.py .
COPY seed/ seed/
USER nobody
CMD ["python", "care_plan_server.py"]
