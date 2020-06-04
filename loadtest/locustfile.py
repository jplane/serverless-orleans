import string
import random
from locust import HttpUser, task, between

class MessageActorUser(HttpUser):
    wait_time = between(0.5, 2)

    @task(2)
    def add_message(self):
        actor_id = random.randint(1, 100)
        self.client.post(f"/messages/{actor_id}", json=self.__get_random_text())

    @task
    def get_messages(self):
        actor_id = random.randint(1, 100)
        self.client.get(f"/messages/{actor_id}")

    def __get_random_text(self):
        length = random.randint(10, 50)
        return ''.join(random.choices(string.ascii_uppercase + string.digits, k = length))
